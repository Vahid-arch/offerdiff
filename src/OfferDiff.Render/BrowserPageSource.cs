using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
using OfferDiff.Core;

namespace OfferDiff.Render;

// Same GetHtml(url) shape as HttpPageSource, but returns the DOM after JavaScript has run, for stores that add
// their JSON-LD in the browser. Failures are ArgumentException or IOException, which Comparer turns into "unknown".
// shortcut: synchronous and sequential, like HttpPageSource; make async when feeds get large
public sealed class BrowserPageSource : IDisposable, IAsyncDisposable
{
    static readonly TimeSpan DefaultNavigationTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DefaultJsonLdWait = TimeSpan.FromSeconds(10);

    readonly HttpPageSource gate;
    readonly HostThrottle throttle;
    readonly bool allowPrivateHosts;
    readonly TimeSpan navigationTimeout;
    readonly TimeSpan jsonLdWait;
    readonly Func<IBrowserContext, Task>? configureContext;
    readonly string? browserChannel;
    readonly SemaphoreSlim startLock = new(1, 1);

    // Browser channels Playwright can launch from the user's own installation instead of its downloaded Chromium.
    public static readonly string[] Channels =
        ["chrome", "chrome-beta", "chrome-dev", "chrome-canary", "msedge", "msedge-beta", "msedge-dev", "msedge-canary"];

    // A script that is true once some JSON-LD block names a Product (or ProductGroup). Waiting for any JSON-LD
    // would stop at an Organization or BreadcrumbList block that many stores render before the product.
    const string ProductJsonLdPresent = """
        () => [...document.querySelectorAll('script[type="application/ld+json"]')]
            .some(s => /"@type"\s*:\s*(\[[^\]]*)?"Product(Group)?"/.test(s.textContent))
        """;

    GuardProxy? proxy;
    IPlaywright? playwright;
    IBrowser? browser;

    // Everything refused by the guard during this run: request URLs from the route, host:port from the proxy.
    internal ConcurrentQueue<string> BlockedRequests { get; } = new();

    // configureContext lets tests register in-memory pages; the guard is added after it, so the guard runs first.
    internal BrowserPageSource(HttpPageSource gate, HostThrottle throttle, bool allowPrivateHosts = false,
        TimeSpan? navigationTimeout = null, TimeSpan? jsonLdWait = null, Func<IBrowserContext, Task>? configureContext = null,
        string? browserChannel = null)
    {
        this.gate = gate;
        this.throttle = throttle;
        this.allowPrivateHosts = allowPrivateHosts;
        this.navigationTimeout = navigationTimeout ?? DefaultNavigationTimeout;
        this.jsonLdWait = jsonLdWait ?? DefaultJsonLdWait;
        this.configureContext = configureContext;
        this.browserChannel = browserChannel;
    }

    // browserChannel: one of Channels, to use an installed Chrome or Edge instead of Playwright's Chromium.
    public static BrowserPageSource Create(bool allowPrivateHosts = false, TimeSpan? minDelay = null, bool respectRobots = true,
        string? browserChannel = null)
    {
        var throttle = new HostThrottle(minDelay ?? TimeSpan.FromSeconds(1));
        return new(new HttpPageSource(HttpPageSource.CreateClient(allowPrivateHosts), throttle, respectRobots), throttle, allowPrivateHosts,
            browserChannel: browserChannel);
    }

    // Starts the browser now instead of on the first page, so a missing browser is reported once, before any row.
    public void Start()
    {
        try
        {
            Task.Run(StartBrowserAsync).GetAwaiter().GetResult();
        }
        catch (PlaywrightException ex)
        {
            throw ToIOException(ex);
        }
    }

    // Downloads Playwright's headless Chromium (the only build OfferDiff launches) into the user's Playwright folder.
    public static int InstallChromium()
    {
        // Without the driver, Playwright's installer prints its own error and returns 1 instead of throwing.
        if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, ".playwright")))
            throw new BrowserMissingException(MissingPart.Driver, "the Playwright driver (the .playwright folder) is missing next to the program");

        try
        {
            return Microsoft.Playwright.Program.Main(["install", "chromium", "--only-shell"]);
        }
        catch (PlaywrightException ex)
        {
            throw ToIOException(ex);
        }
    }

    public string GetHtml(string url) => Task.Run(() => GetHtmlAsync(url)).GetAwaiter().GetResult();

    async Task<string> GetHtmlAsync(string url)
    {
        var uri = gate.CheckBeforeFetch(url);
        await NetworkGuard.ResolveAsync(uri.DnsSafeHost, allowPrivateHosts).ConfigureAwait(false);
        throttle.Wait(uri.Host);

        try
        {
            var chromium = await StartBrowserAsync();
            proxy!.TakeLastBlocked();

            await using var context = await chromium.NewContextAsync(new()
            {
                UserAgent = HttpPageSource.UserAgent,
                // service worker requests would skip the route guard (the proxy would still check them)
                ServiceWorkers = ServiceWorkerPolicy.Block,
            });
            if (configureContext != null) await configureContext(context);
            // on the context, not the page: a page route does not see popups
            await context.RouteAsync("**/*", GuardAsync);

            var page = await context.NewPageAsync();
            var html = await LoadAsync(page, url);
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.IgnoreErrors });

            if (Encoding.UTF8.GetByteCount(html) > HttpPageSource.MaxBytes)
                throw new IOException($"page larger than {HttpPageSource.MaxBytes / 1024 / 1024} MB");
            return html;
        }
        catch (PlaywrightException ex)
        {
            throw ToIOException(ex);
        }
        catch (TimeoutException)
        {
            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"timed out after {navigationTimeout.TotalSeconds:0.##}s"));
        }
    }

    async Task<string> LoadAsync(IPage page, string url)
    {
        IResponse? response;
        try
        {
            // DOMContentLoaded, not load: a slow analytics script should not fail the page; the JSON-LD wait follows
            response = await page.GotoAsync(url, new()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)navigationTimeout.TotalMilliseconds,
            });
        }
        catch (PlaywrightException) when (proxy!.TakeLastBlocked() is { } blocked)
        {
            throw blocked; // the document redirected to a private address
        }

        if (response != null)
        {
            if (!response.Ok)
                throw (Exception?)proxy!.TakeLastBlocked() ?? new IOException($"HTTP {response.Status} {response.StatusText}");

            var type = (await response.AllHeadersAsync()).GetValueOrDefault("content-type", "").Split(';')[0].Trim();
            if (type is not ("text/html" or "application/xhtml+xml"))
                throw new IOException($"not an HTML page ({(type == "" ? "no content type" : type)})");
        }

        try
        {
            await page.WaitForFunctionAsync(ProductJsonLdPresent, null, new() { Timeout = (float)jsonLdWait.TotalMilliseconds });
        }
        catch (TimeoutException)
        {
            // no Product JSON-LD after waiting: return the page anyway; the extractor adds the browser-mode hint
        }

        return await page.ContentAsync();
    }

    async Task GuardAsync(IRoute route)
    {
        var request = route.Request;
        if (request.ResourceType is "image" or "media" or "font")
        {
            await route.AbortAsync();
            return;
        }

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            BlockedRequests.Enqueue(request.Url);
            await route.AbortAsync("blockedbyclient");
            return;
        }

        try
        {
            await NetworkGuard.ResolveAsync(uri.DnsSafeHost, allowPrivateHosts);
        }
        catch (BlockedAddressException)
        {
            BlockedRequests.Enqueue(request.Url);
            await route.AbortAsync("blockedbyclient");
            return;
        }
        catch (SocketException)
        {
            await route.AbortAsync("namenotresolved");
            return;
        }

        await route.FallbackAsync();
    }

    // One headless Chromium per run, started on the first page.
    async Task<IBrowser> StartBrowserAsync()
    {
        await startLock.WaitAsync();
        try
        {
            if (browser != null) return browser;

            // ??= so a failed start (no driver, no Chromium) can be retried without leaking the proxy or driver
            proxy ??= new GuardProxy(allowPrivateHosts, BlockedRequests.Enqueue);
            playwright ??= await Playwright.CreateAsync();
            return browser = await playwright.Chromium.LaunchAsync(new()
            {
                Channel = browserChannel,
                // Playwright adds <-loopback> to the bypass list, so loopback addresses go through the proxy too
                Proxy = new() { Server = $"http://127.0.0.1:{proxy.Port}" },
                // WebRTC uses UDP, which a proxy cannot see
                Args = ["--force-webrtc-ip-handling-policy=disable_non_proxied_udp"],
            });
        }
        finally
        {
            startLock.Release();
        }
    }

    internal static IOException ToIOException(PlaywrightException ex)
    {
        if (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
            return new BrowserMissingException(MissingPart.Chromium, $"Chromium is not installed; run: {InstallCommand()}");
        if (ex.Message.Contains("Driver not found", StringComparison.OrdinalIgnoreCase))
            return new BrowserMissingException(MissingPart.Driver,
                "the Playwright driver (the .playwright folder) is missing next to the program");
        if (ex.Message.Contains("Chromium distribution", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("is not found", StringComparison.OrdinalIgnoreCase))
            return new BrowserMissingException(MissingPart.Channel, ex.Message.Split(" at ")[0].Trim());

        // Playwright messages continue with a long call log after the first line
        return new IOException(ex.Message.Split('\n')[0].Trim());
    }

    internal static string InstallCommand()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
        return OperatingSystem.IsWindows()
            ? $"powershell -ExecutionPolicy Bypass -File \"{script}\" install chromium"
            : $"pwsh \"{script}\" install chromium";
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (browser != null) await browser.DisposeAsync();
        playwright?.Dispose();
        proxy?.Dispose();
        browser = null;
        playwright = null;
        proxy = null;
    }
}

// Which part is missing decides the fix: install Chromium, get the render download, or pick another channel.
public enum MissingPart { Chromium, Driver, Channel }

// Thrown (as an IOException, so a row would still become unknown) when the browser cannot start at all.
public sealed class BrowserMissingException(MissingPart part, string message) : IOException(message)
{
    public MissingPart Part => part;
}

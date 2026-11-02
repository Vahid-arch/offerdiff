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
    readonly SemaphoreSlim startLock = new(1, 1);

    GuardProxy? proxy;
    IPlaywright? playwright;
    IBrowser? browser;

    // Everything refused by the guard during this run: request URLs from the route, host:port from the proxy.
    internal ConcurrentQueue<string> BlockedRequests { get; } = new();

    // configureContext lets tests register in-memory pages; the guard is added after it, so the guard runs first.
    internal BrowserPageSource(HttpPageSource gate, HostThrottle throttle, bool allowPrivateHosts = false,
        TimeSpan? navigationTimeout = null, TimeSpan? jsonLdWait = null, Func<IBrowserContext, Task>? configureContext = null)
    {
        this.gate = gate;
        this.throttle = throttle;
        this.allowPrivateHosts = allowPrivateHosts;
        this.navigationTimeout = navigationTimeout ?? DefaultNavigationTimeout;
        this.jsonLdWait = jsonLdWait ?? DefaultJsonLdWait;
        this.configureContext = configureContext;
    }

    public static BrowserPageSource Create(bool allowPrivateHosts = false, TimeSpan? minDelay = null, bool respectRobots = true)
    {
        var throttle = new HostThrottle(minDelay ?? TimeSpan.FromSeconds(1));
        return new(new HttpPageSource(HttpPageSource.CreateClient(allowPrivateHosts), throttle, respectRobots), throttle, allowPrivateHosts);
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
            // State: Attached, because a script element is never "visible", which is what the default waits for
            await page.WaitForSelectorAsync("script[type=\"application/ld+json\"]", new()
            {
                State = WaitForSelectorState.Attached,
                Timeout = (float)jsonLdWait.TotalMilliseconds,
            });
        }
        catch (TimeoutException)
        {
            // no JSON-LD after waiting: return the page anyway, and the row becomes unknown
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

            proxy = new GuardProxy(allowPrivateHosts, BlockedRequests.Enqueue);
            playwright = await Playwright.CreateAsync();
            return browser = await playwright.Chromium.LaunchAsync(new()
            {
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
            return new IOException($"Chromium is not installed; run: {InstallCommand()}");

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

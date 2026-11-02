using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
using OfferDiff.Core;
using OfferDiff.Render;

// Chromium is heavy, and one test changes an environment variable, so these run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OfferDiff.Render.Tests;

// A real headless Chromium with every page served from memory. 203.0.113.10 is a reserved documentation address
// that NetworkGuard allows and needs no DNS; any request not answered here is aborted, so nothing leaves this PC.
public class BrowserPageSourceTests : IDisposable
{
    const string Site = "http://203.0.113.10";
    const string MugUrl = Site + "/products/mug";

    readonly List<string> served = [];
    readonly List<string> unanswered = [];
    readonly Dictionary<string, Func<IRoute, Task>> pages = [];
    readonly List<BrowserPageSource> sources = [];

    public void Dispose()
    {
        foreach (var source in sources) source.Dispose();
    }

    static string InjectedJsonLd(decimal price) => $$"""
        <!doctype html><html><head><title>Mug</title></head><body><div id="root"></div>
        <script>
          setTimeout(() => {
            const block = document.createElement('script');
            block.type = 'application/ld+json';
            block.textContent = JSON.stringify({
              '@type': 'Product', sku: 'MUG',
              offers: { '@type': 'Offer', url: '{{MugUrl}}', price: {{price}}, priceCurrency: 'USD', availability: 'https://schema.org/InStock' }
            });
            document.head.appendChild(block);
          }, 50);
        </script></body></html>
        """;

    void Serve(string url, string html) =>
        pages[url] = route => route.FulfillAsync(new() { Status = 200, ContentType = "text/html", Body = html });

    BrowserPageSource Source(HostThrottle? throttle = null, HttpPageSource? gate = null, bool allowPrivateHosts = false,
        TimeSpan? navigationTimeout = null, TimeSpan? jsonLdWait = null)
    {
        throttle ??= new HostThrottle(TimeSpan.Zero);
        gate ??= new HttpPageSource(new HttpClient(), throttle, respectRobots: false);
        var source = new BrowserPageSource(gate, throttle, allowPrivateHosts, navigationTimeout, jsonLdWait ?? TimeSpan.FromSeconds(5), context =>
            context.RouteAsync("**/*", route =>
            {
                lock (served) served.Add(route.Request.Url);
                if (pages.TryGetValue(route.Request.Url, out var respond)) return respond(route);
                lock (unanswered) unanswered.Add(route.Request.Url);
                return route.AbortAsync();
            }));
        sources.Add(source);
        return source;
    }

    static FeedItem Mug(decimal price = 12.99m) => new(2, "MUG", "Mug", MugUrl, new Price(price, "USD"), null, "in_stock");

    [Fact]
    public void Javascript_injected_json_ld_gives_match()
    {
        Serve(MugUrl, InjectedJsonLd(12.99m));

        var result = Comparer.Compare(Mug(), Source().GetHtml);

        Assert.Equal(Verdict.Match, result.Verdict);
        Assert.Equal("url", result.MatchedBy);
    }

    [Fact]
    public void Deliberate_price_difference_gives_mismatch()
    {
        Serve(MugUrl, InjectedJsonLd(15.00m));

        var result = Comparer.Compare(Mug(), Source().GetHtml);

        Assert.Equal(Verdict.Mismatch, result.Verdict);
        Assert.Equal("price: feed 12.99, page 15", Assert.Single(result.Details));
    }

    [Fact]
    public void No_json_ld_after_javascript_is_unknown_and_returns_the_rendered_page()
    {
        Serve(MugUrl, "<html><body><script>document.body.dataset.rendered = 'yes';</script></body></html>");
        var source = Source(jsonLdWait: TimeSpan.FromMilliseconds(300));

        var result = Comparer.Compare(Mug(), source.GetHtml);

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Contains("no JSON-LD block on page", result.Details);
        Assert.Contains("data-rendered=\"yes\"", source.GetHtml(MugUrl));
    }

    [Fact]
    public void Sub_requests_to_private_addresses_are_aborted_and_recorded()
    {
        Serve(MugUrl, """
            <html><body><script>
              window.done = Promise.allSettled([
                fetch('http://127.0.0.1/secret', { mode: 'no-cors' }),
                fetch('http://169.254.169.254/latest/meta-data/', { mode: 'no-cors' }),
              ]).then(() => {
                const block = document.createElement('script');
                block.type = 'application/ld+json';
                block.textContent = '{}';
                document.head.appendChild(block);
              });
            </script></body></html>
            """);
        var source = Source();

        source.GetHtml(MugUrl);

        Assert.Contains("http://127.0.0.1/secret", source.BlockedRequests);
        Assert.Contains("http://169.254.169.254/latest/meta-data/", source.BlockedRequests);
        Assert.DoesNotContain("http://127.0.0.1/secret", served); // the guard stopped them before anything else saw them
    }

    [Theory]
    [InlineData("http://127.0.0.1/products/mug", "blocked private address 127.0.0.1")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "blocked private address 169.254.169.254")]
    [InlineData("http://[::1]/products/mug", "blocked private address ::1")]
    public void Main_url_on_a_private_address_throws_blocked(string url, string message)
    {
        var ex = Assert.Throws<BlockedAddressException>(() => Source().GetHtml(url));

        Assert.Equal(message, ex.Message);
        Assert.Empty(served);
    }

    // The next tests use a real loopback socket as the "internal service", to prove nothing ever connects to it.
    sealed class InternalService : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        public volatile int Connections;

        public InternalService()
        {
            listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using var client = await listener.AcceptTcpClientAsync();
                        Connections++;
                    }
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException) { }
            });
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public void Dispose() => listener.Stop();
    }

    [Fact]
    public void Redirect_to_a_private_address_is_blocked_and_never_connects()
    {
        // Route handlers do not see the next hop of a redirect: this is the hole GuardProxy closes.
        using var service = new InternalService();
        var target = $"http://127.0.0.1:{service.Port}/secret";
        pages[MugUrl] = route => route.FulfillAsync(new() { Status = 302, Headers = new Dictionary<string, string> { ["Location"] = target } });
        var source = Source();

        var ex = Assert.Throws<BlockedAddressException>(() => source.GetHtml(MugUrl));

        Assert.Equal("blocked private address 127.0.0.1", ex.Message);
        Assert.Equal(0, service.Connections);
        Assert.DoesNotContain(target, served); // confirms the route never saw this hop
        Assert.Contains($"127.0.0.1:{service.Port}", source.BlockedRequests);
    }

    [Fact]
    public void Redirected_row_becomes_unknown()
    {
        pages[MugUrl] = route => route.FulfillAsync(new() { Status = 302, Headers = new Dictionary<string, string> { ["Location"] = "http://169.254.169.254/latest/meta-data/" } });

        var result = Comparer.Compare(Mug(), Source().GetHtml);

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Equal("page could not be loaded: blocked private address 169.254.169.254", Assert.Single(result.Details));
    }

    [Fact]
    public void WebSocket_to_a_private_address_never_connects()
    {
        using var service = new InternalService();
        Serve(MugUrl, $$"""
            <html><body><script>
              const socket = new WebSocket('ws://127.0.0.1:{{service.Port}}/internal');
              socket.onerror = socket.onclose = () => {
                const block = document.createElement('script');
                block.type = 'application/ld+json';
                block.textContent = '{}';
                document.head.appendChild(block);
              };
            </script></body></html>
            """);
        var source = Source();

        source.GetHtml(MugUrl);

        Assert.Equal(0, service.Connections);
        Assert.Contains($"127.0.0.1:{service.Port}", source.BlockedRequests);
    }

    [Fact]
    public void Popup_to_a_private_address_is_blocked()
    {
        using var service = new InternalService();
        Serve(MugUrl, $$"""
            <html><body><script>
              window.open('http://127.0.0.1:{{service.Port}}/popup');
              setTimeout(() => {
                const block = document.createElement('script');
                block.type = 'application/ld+json';
                block.textContent = '{}';
                document.head.appendChild(block);
              }, 300);
            </script></body></html>
            """);
        var source = Source();

        source.GetHtml(MugUrl);

        Assert.Equal(0, service.Connections);
        Assert.Contains($"http://127.0.0.1:{service.Port}/popup", source.BlockedRequests);
    }

    [Fact]
    public void Allow_private_hosts_lets_private_addresses_through_the_guard()
    {
        Serve("http://127.0.0.1/products/mug", InjectedJsonLd(12.99m));
        var source = Source(allowPrivateHosts: true);

        Assert.Contains("application/ld+json", source.GetHtml("http://127.0.0.1/products/mug"));
        Assert.Empty(source.BlockedRequests);
    }

    [Fact]
    public void Images_media_and_fonts_are_blocked_but_scripts_are_not()
    {
        Serve(MugUrl, $$"""
            <html><head><script src="{{Site}}/app.js"></script></head>
            <body><img src="{{Site}}/photo.jpg"><video src="{{Site}}/clip.mp4"></video>
            <script type="application/ld+json">{}</script></body></html>
            """);
        pages[Site + "/app.js"] = route => route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = "" });

        Source().GetHtml(MugUrl);

        Assert.Contains(Site + "/app.js", served);
        Assert.DoesNotContain(Site + "/photo.jpg", served);
        Assert.DoesNotContain(Site + "/clip.mp4", served);
    }

    [Fact]
    public void Sends_the_OfferDiff_user_agent()
    {
        string? agent = null;
        pages[MugUrl] = async route =>
        {
            agent = route.Request.Headers["user-agent"];
            await route.FulfillAsync(new() { Status = 200, ContentType = "text/html", Body = "<script type=\"application/ld+json\">{}</script>" });
        };

        Source().GetHtml(MugUrl);

        Assert.Equal(HttpPageSource.UserAgent, agent);
    }

    [Fact]
    public void Same_throttle_as_http_two_pages_on_one_site_wait()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sleeps = new List<TimeSpan>();
        var throttle = new HostThrottle(TimeSpan.FromSeconds(1), () => now, duration => { sleeps.Add(duration); now += duration; });
        Serve(MugUrl, InjectedJsonLd(12.99m));
        var source = Source(throttle);

        source.GetHtml(MugUrl);
        source.GetHtml(MugUrl);

        Assert.Equal([TimeSpan.FromSeconds(1)], sleeps);
    }

    sealed class RobotsHandler(string robots) : HttpMessageHandler
    {
        public readonly List<string> Requested = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requested.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(robots) });
        }
    }

    [Fact]
    public void Robots_txt_disallowed_page_is_never_opened()
    {
        var handler = new RobotsHandler("User-agent: *\nDisallow: /products/\n");
        var throttle = new HostThrottle(TimeSpan.Zero);
        Serve(MugUrl, InjectedJsonLd(12.99m));

        var result = Comparer.Compare(Mug(), Source(throttle, new HttpPageSource(new HttpClient(handler), throttle)).GetHtml);

        Assert.Equal("page could not be loaded: blocked by robots.txt", Assert.Single(result.Details));
        Assert.Equal(["/robots.txt"], handler.Requested);
        Assert.Empty(served);
    }

    [Theory]
    [InlineData("ftp://example-store.test/a")]
    [InlineData("file:///C:/Windows/win.ini")]
    public void Non_http_urls_are_rejected_without_a_browser(string url)
    {
        Assert.Throws<ArgumentException>(() => Source().GetHtml(url));
    }

    [Fact]
    public void Error_status_is_a_short_IOException()
    {
        pages[MugUrl] = route => route.FulfillAsync(new() { Status = 404, ContentType = "text/html", Body = "gone" });

        Assert.Equal("HTTP 404 Not Found", Assert.ThrowsAny<IOException>(() => Source().GetHtml(MugUrl)).Message);
    }

    [Fact]
    public void Non_html_page_is_a_short_IOException()
    {
        pages[MugUrl] = route => route.FulfillAsync(new() { Status = 200, ContentType = "text/plain; charset=utf-8", Body = "plain" });

        Assert.Equal("not an HTML page (text/plain)", Assert.ThrowsAny<IOException>(() => Source().GetHtml(MugUrl)).Message);
    }

    [Fact]
    public void Page_over_5_MB_is_a_short_IOException()
    {
        Serve(MugUrl, "<script type=\"application/ld+json\">{}</script><p>" + new string('x', HttpPageSource.MaxBytes) + "</p>");

        Assert.Equal("page larger than 5 MB", Assert.ThrowsAny<IOException>(() => Source().GetHtml(MugUrl)).Message);
    }

    [Fact]
    public void Navigation_timeout_is_a_short_IOException()
    {
        var never = new TaskCompletionSource();
        pages[MugUrl] = _ => never.Task;

        Assert.Equal("timed out after 0.3s", Assert.ThrowsAny<IOException>(() => Source(navigationTimeout: TimeSpan.FromMilliseconds(300)).GetHtml(MugUrl)).Message);
    }

    [Fact]
    public void Missing_chromium_gives_the_install_command()
    {
        var empty = Directory.CreateTempSubdirectory("offerdiff-no-browsers");
        var before = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", empty.FullName);
        try
        {
            Serve(MugUrl, InjectedJsonLd(12.99m));

            var ex = Assert.ThrowsAny<IOException>(() => Source().GetHtml(MugUrl));

            Assert.Equal($"Chromium is not installed; run: {BrowserPageSource.InstallCommand()}", ex.Message);
            Assert.Contains("playwright.ps1", ex.Message);
            Assert.Contains(" install chromium", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", before);
            empty.Delete(recursive: true);
        }
    }
}

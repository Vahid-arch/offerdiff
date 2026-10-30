using System.Net;
using System.Text;
using OfferDiff.Core;

namespace OfferDiff.Tests;

// robots.txt handling inside HttpPageSource: a fake handler answers by path, on a fake clock. No network, no waiting.
public class HttpPageSourceRobotsTests
{
    const string Site = "https://example-store.test";

    class RoutingHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Requested = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requested.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request.RequestUri.PathAndQuery));
        }
    }

    readonly FakeClock clock = new();

    (HttpPageSource Source, RoutingHandler Handler) Site_with(Func<HttpResponseMessage> robots)
    {
        var handler = new RoutingHandler(path => path == "/robots.txt" ? robots() : Page());
        return (new HttpPageSource(new HttpClient(handler), clock.Throttle()), handler);
    }

    static HttpResponseMessage Robots(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };

    static HttpResponseMessage Page() => new(HttpStatusCode.OK) { Content = new StringContent("<html>ok</html>", Encoding.UTF8, "text/html") };

    [Fact]
    public void Robots_txt_is_fetched_once_per_site()
    {
        var (source, handler) = Site_with(() => Robots("User-agent: *\nDisallow: /private\n"));

        source.GetHtml(Site + "/products/a");
        source.GetHtml(Site + "/products/b");
        source.GetHtml(Site + "/products/c?variant=2");

        Assert.Equal(["/robots.txt", "/products/a", "/products/b", "/products/c?variant=2"], handler.Requested);
    }

    [Fact]
    public void Robots_request_goes_through_the_throttle()
    {
        var (source, _) = Site_with(() => Robots(""));

        source.GetHtml(Site + "/products/a");

        Assert.Equal([TimeSpan.FromSeconds(1)], clock.Sleeps); // the page waits 1s after robots.txt on the same host
    }

    [Fact]
    public void Missing_robots_txt_allows_the_page()
    {
        var (source, handler) = Site_with(() => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal("<html>ok</html>", source.GetHtml(Site + "/products/a"));
        Assert.Equal(["/robots.txt", "/products/a"], handler.Requested);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "robots.txt unavailable (HTTP 503)")]
    [InlineData(HttpStatusCode.InternalServerError, "robots.txt unavailable (HTTP 500)")]
    [InlineData(HttpStatusCode.TooManyRequests, "robots.txt unavailable (HTTP 429)")]
    public void Server_error_on_robots_disallows_the_whole_site(HttpStatusCode status, string message)
    {
        var (source, handler) = Site_with(() => new HttpResponseMessage(status));

        Assert.Equal(message, Assert.ThrowsAny<IOException>(() => source.GetHtml(Site + "/products/a")).Message);
        Assert.Equal(message, Assert.ThrowsAny<IOException>(() => source.GetHtml(Site + "/products/b")).Message);
        Assert.Equal(["/robots.txt"], handler.Requested); // remembered for the run, pages never requested
    }

    [Fact]
    public void Network_error_on_robots_disallows_the_site()
    {
        var (source, _) = Site_with(() => throw new HttpRequestException("No such host is known."));

        Assert.Equal("robots.txt unavailable: No such host is known.",
            Assert.ThrowsAny<IOException>(() => source.GetHtml(Site + "/products/a")).Message);
    }

    [Fact]
    public void Disallowed_page_is_never_requested()
    {
        var (source, handler) = Site_with(() => Robots("User-agent: OfferDiff\nDisallow: /products/secret\n"));

        Assert.Equal("blocked by robots.txt", Assert.ThrowsAny<IOException>(() => source.GetHtml(Site + "/products/secret")).Message);
        Assert.Equal(["/robots.txt"], handler.Requested);
    }

    [Fact]
    public void Each_origin_has_its_own_robots_txt()
    {
        var (source, handler) = Site_with(() => Robots(""));

        source.GetHtml(Site + "/products/a");
        source.GetHtml("https://other-store.test/products/a");
        source.GetHtml("https://example-store.test:8443/products/a");

        Assert.Equal(3, handler.Requested.Count(p => p == "/robots.txt"));
    }

    [Fact]
    public void Respect_robots_false_skips_robots_txt()
    {
        var handler = new RoutingHandler(_ => Page());
        var source = new HttpPageSource(new HttpClient(handler), clock.Throttle(), respectRobots: false);

        source.GetHtml(Site + "/products/a");

        Assert.Equal(["/products/a"], handler.Requested);
    }

    [Fact]
    public void Disallowed_row_is_unknown_end_to_end()
    {
        var (source, _) = Site_with(() => Robots("User-agent: *\nDisallow: /products/\n"));
        var item = new FeedItem(2, "MUG-BLUE", "Blue Mug", Site + "/products/blue-mug", new Price(12.99m, "USD"), null, "in_stock");

        var result = Comparer.Compare(item, source.GetHtml);

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Equal("page could not be loaded: blocked by robots.txt", Assert.Single(result.Details));
    }
}

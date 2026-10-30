using System.Diagnostics;
using System.Net;
using System.Text;
using OfferDiff.Core;

namespace OfferDiff.Tests;

public class HttpPageSourceTests
{
    const string Url = "https://example-store.test/products/blue-mug";

    // Answers every request in memory: no network, no sockets.
    class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastRequest = request;
            return respond(token);
        }
    }

    // A body that never ends, to prove the reader stops at the size limit instead of loading everything.
    class EndlessStream : Stream
    {
        public long BytesRead;
        public override int Read(byte[] buffer, int offset, int count) { BytesRead += count; return count; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    static (HttpPageSource Source, FakeHandler Handler) Fake(HttpContent content, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = content }));
        return (new HttpPageSource(new HttpClient(handler)), handler);
    }

    static HttpContent Html(string body) => new StringContent(body, Encoding.UTF8, "text/html");

    [Fact]
    public void Returns_html_on_200()
    {
        var (source, _) = Fake(Html("<html>hello</html>"));

        Assert.Equal("<html>hello</html>", source.GetHtml(Url));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404 Not Found")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500 Internal Server Error")]
    public void Error_status_is_IOException_with_status(HttpStatusCode status, string message)
    {
        var (source, _) = Fake(Html("oops"), status);

        Assert.Equal(message, Assert.Throws<IOException>(() => source.GetHtml(Url)).Message);
    }

    [Fact]
    public void Timeout_is_IOException()
    {
        var handler = new FakeHandler(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });
        var source = new HttpPageSource(new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) });

        Assert.Equal("timed out after 0.1s", Assert.Throws<IOException>(() => source.GetHtml(Url)).Message);
    }

    [Fact]
    public void Network_error_is_IOException_with_short_message()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("No such host is known."));
        var source = new HttpPageSource(new HttpClient(handler));

        Assert.Equal("No such host is known.", Assert.Throws<IOException>(() => source.GetHtml(Url)).Message);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("image/png")]
    public void Non_html_content_type_is_IOException(string mediaType)
    {
        var (source, _) = Fake(new StringContent("{}", Encoding.UTF8, mediaType));

        Assert.Equal($"not an HTML page ({mediaType})", Assert.Throws<IOException>(() => source.GetHtml(Url)).Message);
    }

    [Fact]
    public void Body_over_limit_with_content_length_is_IOException()
    {
        var (source, _) = Fake(Html(new string('x', HttpPageSource.MaxBytes + 1)));

        Assert.Equal("page larger than 5 MB", Assert.Throws<IOException>(() => source.GetHtml(Url)).Message);
    }

    [Fact]
    public void Body_over_limit_without_content_length_stops_reading()
    {
        var stream = new EndlessStream();
        var content = new StreamContent(stream);
        content.Headers.ContentType = new("text/html");
        var (source, _) = Fake(content);

        Assert.Equal("page larger than 5 MB", Assert.Throws<IOException>(() => source.GetHtml(Url)).Message);
        Assert.InRange(stream.BytesRead, HttpPageSource.MaxBytes, HttpPageSource.MaxBytes + 1024 * 1024);
    }

    [Theory]
    [InlineData("ftp://example-store.test/products/blue-mug")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("products/blue-mug")]
    public void Non_http_urls_are_ArgumentException_and_not_fetched(string url)
    {
        var (source, handler) = Fake(Html(""));

        Assert.Throws<ArgumentException>(() => source.GetHtml(url));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public void Sends_OfferDiff_user_agent()
    {
        var (source, handler) = Fake(Html(""));

        source.GetHtml(Url);

        Assert.Equal(HttpPageSource.UserAgent, handler.LastRequest!.Headers.UserAgent.ToString());
    }

    static readonly FeedItem BlueMug = new(2, "MUG-BLUE", "Blue Mug", Url, new Price(12.99m, "USD"), null, "in_stock");

    [Fact]
    public void End_to_end_live_page_matches()
    {
        var (source, _) = Fake(Html(File.ReadAllText(Path.Combine(Samples.Pages, "products", "blue-mug.html"))));

        Assert.Equal(Verdict.Match, Comparer.Compare(BlueMug, source.GetHtml).Verdict);
    }

    [Fact]
    public void End_to_end_404_is_unknown()
    {
        var (source, _) = Fake(Html("gone"), HttpStatusCode.NotFound);

        var result = Comparer.Compare(BlueMug, source.GetHtml);

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.StartsWith("page could not be loaded: HTTP 404", Assert.Single(result.Details));
    }
}

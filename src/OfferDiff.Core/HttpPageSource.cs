using System.Globalization;
using System.Net;
using System.Text;

namespace OfferDiff.Core;

// Live counterpart of FixturePageSource: same GetHtml(url) shape, so Comparer.Compare(item, source.GetHtml) works unchanged.
// Every failure is an ArgumentException (bad URL) or IOException (fetch failed), which Comparer turns into "unknown".
// shortcut: synchronous and sequential, fine for small feeds; make async and parallel per
// host when feeds get large
public class HttpPageSource(HttpClient client)
{
    public const string UserAgent = "OfferDiff/0.2 (+https://github.com/Vahid-arch/offerdiff)";
    public const int MaxBytes = 5 * 1024 * 1024;

    // allowPrivateHosts: true is for checking your own staging server on the local network.
    public static HttpPageSource Create(bool allowPrivateHosts = false) => new(new HttpClient(new SocketsHttpHandler
    {
        MaxAutomaticRedirections = 5,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = (context, token) => NetworkGuard.ConnectAsync(context.DnsEndPoint, allowPrivateHosts, token),
        // through a proxy the callback would only see the proxy's address, so the guard would be bypassed
        UseProxy = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    });

    public string GetHtml(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"not an http(s) URL: {url}");

        try
        {
            // No synchronization context in a console app, so blocking on the task cannot deadlock.
            return FetchAsync(uri).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"timed out after {client.Timeout.TotalSeconds:0.##}s"));
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException blocked)
        {
            throw blocked; // keep "blocked private address <ip>" without HttpClient's "(host:port)" suffix
        }
        catch (HttpRequestException ex)
        {
            throw new IOException(ex.Message);
        }
    }

    async Task<string> FetchAsync(Uri uri)
    {
        // HttpClient.Timeout stops counting once headers arrive; this token also covers reading the body.
        using var timeout = new CancellationTokenSource(client.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("text/html" or "application/xhtml+xml"))
            throw new IOException($"not an HTML page ({type ?? "no content type"})");
        if (response.Content.Headers.ContentLength > MaxBytes)
            throw TooLarge();

        // Content-Length can be missing or wrong, so count while reading and stop at the limit.
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        var page = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (page.Length + read > MaxBytes) throw TooLarge();
            page.Write(chunk, 0, read);
        }

        // shortcut: always decodes as UTF-8, honor the charset header if non-UTF-8 stores show garbled values
        return Encoding.UTF8.GetString(page.GetBuffer(), 0, (int)page.Length);
    }

    static IOException TooLarge() => new($"page larger than {MaxBytes / 1024 / 1024} MB");
}

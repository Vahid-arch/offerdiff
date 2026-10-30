using System.Globalization;
using System.Net;
using System.Text;

namespace OfferDiff.Core;

// Live counterpart of FixturePageSource: same GetHtml(url) shape, so Comparer.Compare(item, source.GetHtml) works unchanged.
// Every failure is an ArgumentException (bad URL) or IOException (fetch failed), which Comparer turns into "unknown".
// shortcut: synchronous and sequential, fine for small feeds; make async and parallel per
// host when feeds get large
public class HttpPageSource(HttpClient client, HostThrottle? throttle = null, bool respectRobots = true)
{
    public const string UserAgent = "OfferDiff/0.2.0 (+https://github.com/Vahid-arch/offerdiff)";
    public const string ProductToken = "OfferDiff";
    public const int MaxBytes = 5 * 1024 * 1024;
    static readonly TimeSpan DefaultMinDelay = TimeSpan.FromSeconds(1);
    static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(5);

    readonly HostThrottle throttle = throttle ?? new(DefaultMinDelay);
    // one robots.txt per origin for the whole run; a failed fetch is remembered too, so the site is not asked again
    readonly Dictionary<string, RobotsEntry> robotsByOrigin = new(StringComparer.OrdinalIgnoreCase);

    // allowPrivateHosts: true is for checking your own staging server on the local network.
    // minDelay: shortest gap between two requests to the same host (default 1 second).
    // respectRobots: false skips robots.txt, only for sites you own.
    public static HttpPageSource Create(bool allowPrivateHosts = false, TimeSpan? minDelay = null, bool respectRobots = true) =>
        new(new HttpClient(new SocketsHttpHandler
        {
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = (context, token) => NetworkGuard.ConnectAsync(context.DnsEndPoint, allowPrivateHosts, token),
            // through a proxy the callback would only see the proxy's address, so the guard would be bypassed
            UseProxy = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
        }, new HostThrottle(minDelay ?? DefaultMinDelay), respectRobots);

    public string GetHtml(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"not an http(s) URL: {url}");

        if (respectRobots) CheckRobots(uri);

        for (var attempt = 1; ; attempt++)
        {
            throttle.Wait(uri.Host);
            try
            {
                return Run(() => FetchPageAsync(uri));
            }
            // 429/503 with a short Retry-After: wait and retry once; otherwise the IOException ends the row as unknown
            catch (RateLimitedException limited) when (attempt == 1 && limited.RetryAfter <= MaxRetryAfter)
            {
                throttle.Sleep(limited.RetryAfter);
            }
        }
    }

    void CheckRobots(Uri uri)
    {
        var origin = uri.GetLeftPart(UriPartial.Authority); // scheme://host[:port]
        if (!robotsByOrigin.TryGetValue(origin, out var entry))
            robotsByOrigin[origin] = entry = LoadRobots(new Uri(origin + "/robots.txt"));

        if (entry.Error != null) throw new IOException(entry.Error);
        if (!entry.Rules!.IsAllowed(ProductToken, uri.PathAndQuery)) throw new IOException("blocked by robots.txt");
    }

    RobotsEntry LoadRobots(Uri robotsUri)
    {
        throttle.Wait(robotsUri.Host);
        try
        {
            return Run(() => FetchRobotsAsync(robotsUri));
        }
        catch (BlockedAddressException)
        {
            throw; // the site itself is off-limits; report that, not a robots.txt problem
        }
        catch (IOException ex)
        {
            // RFC 9309: if robots.txt cannot be reached, treat the whole site as disallowed
            return new(null, $"robots.txt unavailable: {ex.Message}");
        }
    }

    // Blocks on the async fetch and turns HttpClient failures into short IOExceptions.
    T Run<T>(Func<Task<T>> fetch)
    {
        try
        {
            // No synchronization context in a console app, so blocking on the task cannot deadlock.
            return fetch().GetAwaiter().GetResult();
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

    async Task<string> FetchPageAsync(Uri uri)
    {
        // HttpClient.Timeout stops counting once headers arrive; this token also covers reading the body.
        using var timeout = new CancellationTokenSource(client.Timeout);
        using var response = await SendAsync(uri, timeout.Token);
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            throw new RateLimitedException((int)response.StatusCode, RetryAfter(response));
        if (!response.IsSuccessStatusCode)
            throw new IOException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("text/html" or "application/xhtml+xml"))
            throw new IOException($"not an HTML page ({type ?? "no content type"})");

        return await ReadBodyAsync(response, MaxBytes, failIfLarger: true, timeout.Token);
    }

    async Task<RobotsEntry> FetchRobotsAsync(Uri robotsUri)
    {
        using var timeout = new CancellationTokenSource(client.Timeout);
        using var response = await SendAsync(robotsUri, timeout.Token);
        var status = (int)response.StatusCode;

        // RFC 9309: 4xx means there is no robots.txt, so everything is allowed.
        // 429 is a 4xx but means "slow down", so it is treated like a server error to stay polite.
        if (status is >= 400 and < 500 && status != 429) return new(RobotsTxt.Parse(""), null);
        if (!response.IsSuccessStatusCode) return new(null, $"robots.txt unavailable (HTTP {status})");

        return new(RobotsTxt.Parse(await ReadBodyAsync(response, RobotsTxt.MaxChars, failIfLarger: false, timeout.Token)), null);
    }

    async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    // Reads at most maxBytes. Pages over the limit are an error; robots.txt is simply cut off there.
    static async Task<string> ReadBodyAsync(HttpResponseMessage response, int maxBytes, bool failIfLarger, CancellationToken token)
    {
        if (failIfLarger && response.Content.Headers.ContentLength > maxBytes)
            throw TooLarge();

        // Content-Length can be missing or wrong, so count while reading and stop at the limit.
        await using var body = await response.Content.ReadAsStreamAsync(token);
        var page = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, token)) > 0)
        {
            if (page.Length + read > maxBytes)
            {
                if (failIfLarger) throw TooLarge();
                page.Write(chunk, 0, maxBytes - (int)page.Length);
                break;
            }
            page.Write(chunk, 0, read);
        }

        // shortcut: always decodes as UTF-8, honor the charset header if non-UTF-8 stores show garbled values
        return Encoding.UTF8.GetString(page.GetBuffer(), 0, (int)page.Length);
    }

    static IOException TooLarge() => new($"page larger than {MaxBytes / 1024 / 1024} MB");

    // Retry-After is either seconds ("2") or an HTTP date; missing or unparseable means the default.
    TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date - throttle.Now) ?? DefaultRetryAfter;
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }

    // Either parsed rules, or the reason robots.txt could not be used (then every page on that origin is unknown).
    sealed record RobotsEntry(RobotsTxt? Rules, string? Error);

    sealed class RateLimitedException(int status, TimeSpan retryAfter)
        : IOException(string.Create(CultureInfo.InvariantCulture,
            $"rate limited by site (HTTP {status}), retry after {Math.Ceiling(retryAfter.TotalSeconds)}s"))
    {
        public TimeSpan RetryAfter => retryAfter;
    }
}

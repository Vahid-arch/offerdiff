using System.Net;
using System.Net.Sockets;
using System.Text;
using OfferDiff.Core;

namespace OfferDiff.Render;

// Playwright routes do not see redirect hops or WebSockets, so route checks alone would let a public page
// redirect the browser to 169.254.169.254. Chromium therefore sends all traffic through this loopback proxy,
// and every connection goes through NetworkGuard: it resolves once, checks, and connects only to the checked
// addresses, so a second DNS answer cannot swap in a private one either.
sealed class GuardProxy : IDisposable
{
    const int MaxHeadBytes = 64 * 1024;

    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly bool allowPrivateHosts;
    readonly Action<string> onBlocked;
    BlockedAddressException? lastBlocked;

    public GuardProxy(bool allowPrivateHosts, Action<string> onBlocked)
    {
        this.allowPrivateHosts = allowPrivateHosts;
        this.onBlocked = onBlocked;
        listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    // The newest block since the last call; the caller uses it to explain a failed main document.
    public BlockedAddressException? TakeLastBlocked() => Interlocked.Exchange(ref lastBlocked, null);

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        stop.Dispose();
    }

    async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
                _ = HandleAsync(await listener.AcceptTcpClientAsync(stop.Token));
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // stopped by Dispose
        }
    }

    async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var token = stop.Token;
                var browser = client.GetStream();
                if (await ReadHeadAsync(browser, token) is not { } head) return;

                var lines = head.Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length != 3) { await ReplyAsync(browser, "400 Bad Request", token); return; }
                var (method, target, version) = (request[0], request[1], request[2]);

                var isTunnel = method == "CONNECT";
                if (!Uri.TryCreate(isTunnel ? $"https://{target}" : target, UriKind.Absolute, out var uri)
                    || (!isTunnel && uri.Scheme != Uri.UriSchemeHttp))
                {
                    await ReplyAsync(browser, "400 Bad Request", token);
                    return;
                }

                Stream site;
                try
                {
                    site = await NetworkGuard.ConnectAsync(new DnsEndPoint(uri.DnsSafeHost, uri.Port), allowPrivateHosts, token);
                }
                catch (BlockedAddressException blocked)
                {
                    lastBlocked = blocked;
                    onBlocked($"{uri.Host}:{uri.Port}");
                    await ReplyAsync(browser, "403 Forbidden", token);
                    return;
                }
                catch (SocketException)
                {
                    await ReplyAsync(browser, "502 Bad Gateway", token);
                    return;
                }

                await using (site)
                {
                    if (isTunnel)
                        await ReplyAsync(browser, "200 Connection Established", token);
                    else
                        await site.WriteAsync(Encoding.Latin1.GetBytes(ToOriginForm(lines, method, uri, version)), token);

                    // One connection serves one site: "Connection: close" stops the browser reusing it for another host.
                    await Task.WhenAny(browser.CopyToAsync(site, token), site.CopyToAsync(browser, token));
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // the browser or the site hung up, or the proxy is stopping
            }
        }
    }

    // "GET http://host/path" to a proxy becomes "GET /path" to the site, without the proxy's own headers.
    static string ToOriginForm(string[] lines, string method, Uri uri, string version)
    {
        var head = new StringBuilder($"{method} {uri.PathAndQuery} {version}\r\n");
        foreach (var line in lines.Skip(1).Where(l => l != ""))
            if (!line.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase))
                head.Append(line).Append("\r\n");
        return head.Append("Connection: close\r\n\r\n").ToString();
    }

    // Reads one byte at a time so nothing after the head (a request body, TLS bytes) is consumed.
    static async Task<string?> ReadHeadAsync(Stream stream, CancellationToken token)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < MaxHeadBytes && await stream.ReadAsync(one, token) == 1)
        {
            head.Add(one[0]);
            if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
                return Encoding.Latin1.GetString(head.ToArray());
        }
        return null;
    }

    static async Task ReplyAsync(Stream stream, string status, CancellationToken token) =>
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\n\r\n"), token);
}

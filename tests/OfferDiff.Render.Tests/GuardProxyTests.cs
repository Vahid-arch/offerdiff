using System.Net;
using System.Net.Sockets;
using System.Text;
using OfferDiff.Core;
using OfferDiff.Render;

namespace OfferDiff.Render.Tests;

// Talks to the proxy directly over loopback, the way Chromium does. The "site" is a local socket: no real network.
public class GuardProxyTests
{
    readonly List<string> blocked = [];

    // One connection: send one request, return everything until the far side closes.
    static async Task<string> SendAsync(int port, string request, string? thenSend = null)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        var buffer = new byte[4096];
        var received = new StringBuilder();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (thenSend != null && received.ToString().EndsWith("\r\n\r\n"))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(thenSend));
                thenSend = null;
            }
        }
        return received.ToString();
    }

    // A local site that records the request it receives and answers once, then closes.
    static (TcpListener Listener, Task<string> Received) Site(string answer)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
            return Encoding.ASCII.GetString(buffer, 0, read);
        }));
    }

    [Theory]
    [InlineData("CONNECT 127.0.0.1:{0} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")]
    [InlineData("GET http://127.0.0.1:{0}/secret HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")]
    public async Task Private_target_gets_403_and_is_never_contacted(string request)
    {
        var (listener, received) = Site("nope");
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var proxy = new GuardProxy(allowPrivateHosts: false, blocked.Add);
        try
        {
            var reply = await SendAsync(proxy.Port, string.Format(request, port));

            Assert.StartsWith("HTTP/1.1 403", reply);
            Assert.False(received.IsCompleted);
            Assert.Single(blocked);
            Assert.Equal("blocked private address 127.0.0.1", proxy.TakeLastBlocked()!.Message);
            Assert.Null(proxy.TakeLastBlocked()); // taken once
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Metadata_address_is_refused_without_a_connection_attempt()
    {
        using var proxy = new GuardProxy(allowPrivateHosts: false, blocked.Add);

        var reply = await SendAsync(proxy.Port, "CONNECT 169.254.169.254:80 HTTP/1.1\r\nHost: x\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403", reply);
        Assert.Equal(["169.254.169.254:80"], blocked);
    }

    [Fact]
    public async Task Allowed_http_request_is_forwarded_in_origin_form_without_proxy_headers()
    {
        var (listener, received) = Site("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var proxy = new GuardProxy(allowPrivateHosts: true, blocked.Add);
        try
        {
            var reply = await SendAsync(proxy.Port,
                $"GET http://127.0.0.1:{port}/products/mug?variant=2 HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nProxy-Connection: keep-alive\r\nConnection: keep-alive\r\nUser-Agent: test\r\n\r\n");

            Assert.EndsWith("ok", reply);
            var request = await received;
            Assert.StartsWith("GET /products/mug?variant=2 HTTP/1.1\r\n", request);
            Assert.Contains("User-Agent: test\r\n", request);
            Assert.Contains("Connection: close\r\n", request);
            Assert.DoesNotContain("Proxy-Connection", request);
            Assert.DoesNotContain("keep-alive", request);
            Assert.Empty(blocked);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Allowed_connect_tunnels_bytes_both_ways()
    {
        var (listener, received) = Site("from-site");
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var proxy = new GuardProxy(allowPrivateHosts: true, blocked.Add);
        try
        {
            var reply = await SendAsync(proxy.Port, $"CONNECT 127.0.0.1:{port} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n", thenSend: "from-browser");

            Assert.StartsWith("HTTP/1.1 200", reply);
            Assert.EndsWith("from-site", reply);
            Assert.Equal("from-browser", await received);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("nonsense\r\n\r\n")]
    [InlineData("GET ftp://127.0.0.1/x HTTP/1.1\r\n\r\n")]
    [InlineData("GET /relative HTTP/1.1\r\n\r\n")]
    public async Task Malformed_requests_get_400(string request)
    {
        using var proxy = new GuardProxy(allowPrivateHosts: true, blocked.Add);

        Assert.StartsWith("HTTP/1.1 400", await SendAsync(proxy.Port, request));
    }

    // Keep-alive: a browser may send a second request on a proxy connection it already used. The proxy binds each
    // connection to the first site it checked, so a second request naming a private address must never open a new
    // connection there. 203.0.113.10 stands in for a public store: the test's connect function maps it to a local
    // socket; every other address goes through the real NetworkGuard, which blocks loopback.
    [Theory]
    [InlineData(true)]  // both requests in one write (pipelined)
    [InlineData(false)] // second request after the first response
    public async Task Reused_keep_alive_connection_cannot_reach_a_private_address(bool pipelined)
    {
        var (store, storeReceived) = GracefulSite("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 2\r\n\r\nok");
        var storePort = ((IPEndPoint)store.LocalEndpoint).Port;
        var privateService = new TcpListener(IPAddress.Loopback, 0);
        privateService.Start();
        var privatePort = ((IPEndPoint)privateService.LocalEndpoint).Port;
        var privateConnection = privateService.AcceptTcpClientAsync();

        var connects = new List<string>();
        async ValueTask<Stream> Connect(DnsEndPoint endpoint, CancellationToken token)
        {
            lock (connects) connects.Add($"{endpoint.Host}:{endpoint.Port}");
            if (endpoint.Host != "203.0.113.10")
                return await NetworkGuard.ConnectAsync(endpoint, allowPrivateHosts: false, token);
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, storePort, token);
            return client.GetStream();
        }

        using var proxy = new GuardProxy(allowPrivateHosts: false, blocked.Add, Connect);
        var first = "GET http://203.0.113.10/products/mug HTTP/1.1\r\nHost: 203.0.113.10\r\nProxy-Connection: keep-alive\r\n\r\n";
        var second = $"GET http://127.0.0.1:{privatePort}/secret HTTP/1.1\r\nHost: 127.0.0.1\r\nProxy-Connection: keep-alive\r\n\r\n";
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(pipelined ? first + second : first));

            var reply = await ReadToCloseAsync(stream);
            if (!pipelined)
            {
                // the proxy closed the connection after the first response, so the second request has nowhere to go
                try { await stream.WriteAsync(Encoding.ASCII.GetBytes(second)); } catch (IOException) { }
                Assert.Equal("", await ReadToCloseAsync(stream));
            }

            Assert.EndsWith("ok", reply);
            Assert.StartsWith("GET /products/mug HTTP/1.1\r\n", await storeReceived);
            Assert.Equal(["203.0.113.10:80"], connects); // one upstream connection only, never a second one
            Assert.False(privateConnection.IsCompleted);  // nothing ever connected to the private service
            Assert.Empty(blocked);                         // it was never even attempted
        }
        finally
        {
            store.Stop();
            privateService.Stop();
        }
    }

    // Like a real server answering "Connection: close": reply, end its side, and drain whatever else arrives.
    // (Closing with unread bytes would make Windows send a TCP reset that can drop the reply.)
    static (TcpListener Listener, Task<string> Received) GracefulSite(string answer)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            var request = Encoding.ASCII.GetString(buffer, 0, read);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
            client.Client.Shutdown(SocketShutdown.Send);
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { while (await stream.ReadAsync(buffer, drain.Token) > 0) { } } catch (OperationCanceledException) { } catch (IOException) { }
            return request;
        }));
    }

    static async Task<string> ReadToCloseAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var received = new StringBuilder();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
        catch (IOException)
        {
            // connection reset after close counts as closed
        }
        return received.ToString();
    }
}

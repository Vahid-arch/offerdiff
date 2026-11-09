using System.Net;
using System.Net.Sockets;

namespace OfferDiff.Core;

// A feed decides which URLs get fetched, so a link to http://192.168.1.1/ or a cloud metadata address
// must not reach the local network (SSRF). The check runs on every connection, including redirects.
public static class NetworkGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4(); // ::ffff:0:0/96
        // a NAT64 gateway, 6to4 relay or Teredo relay would deliver to the IPv4 address inside, so check that too
        if (EmbeddedIPv4(address) is { } embedded && IsBlocked(embedded)) return true;
        if (IPAddress.IsLoopback(address)) return true; // 127.0.0.0/8, ::1

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                              // 0.0.0.0/8 "this network", includes 0.0.0.0
                || b[0] == 10                             // 10.0.0.0/8
                || (b[0] == 172 && (b[1] & 0xF0) == 16)   // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)           // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254)           // 169.254.0.0/16 link-local, cloud metadata
                || (b[0] == 100 && (b[1] & 0xC0) == 64)   // 100.64.0.0/10 carrier-grade NAT
                || b[0] >= 224;                           // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved, broadcast
        }

        return address.Equals(IPAddress.IPv6Any)          // ::
            || address.IsIPv6LinkLocal                    // fe80::/10
            || address.IsIPv6SiteLocal                    // fec0::/10, deprecated but still private
            || address.IsIPv6Multicast                    // ff00::/8
            || (address.GetAddressBytes()[0] & 0xFE) == 0xFC; // fc00::/7 unique-local
    }

    // The IPv4 address carried inside a NAT64, 6to4 or Teredo IPv6 address, or null for any other address.
    // (IPv4-mapped ::ffff:a.b.c.d is handled by MapToIPv4 in IsBlocked.)
    // shortcut: 64:ff9b:1::/48 is decoded with the IPv4 in the last 32 bits (the common /96 layout); RFC 6052 also
    // allows /48-/64 layouts there, decode those too if a network that uses them is ever in scope
    public static IPAddress? EmbeddedIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return null;
        var b = address.GetAddressBytes();

        // NAT64 well-known prefix 64:ff9b::/96: the IPv4 is the last 32 bits
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4..12].All(x => x == 0))
            return new IPAddress(b[12..16]);
        // NAT64 local-use prefix 64:ff9b:1::/48 (RFC 8215)
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4] == 0x00 && b[5] == 0x01)
            return new IPAddress(b[12..16]);
        // 6to4 2002::/16: the IPv4 is bits 16-47
        if (b[0] == 0x20 && b[1] == 0x02)
            return new IPAddress(b[2..6]);
        // Teredo 2001::/32: the client's IPv4 is the last 32 bits with every bit flipped
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
            return new IPAddress(b[12..16].Select(x => (byte)~x).ToArray());

        return null;
    }

    // Rejects the host if ANY of its addresses is blocked, so a mixed DNS answer cannot sneak one through.
    public static async Task<IPAddress[]> ResolveAsync(string host, bool allowPrivateHosts, CancellationToken token = default)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, token);
        if (!allowPrivateHosts)
            foreach (var address in addresses)
                if (IsBlocked(address))
                    throw new BlockedAddressException(address);
        return addresses;
    }

    // Used as SocketsHttpHandler.ConnectCallback: resolve, check, then connect only to the checked addresses.
    public static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, bool allowPrivateHosts, CancellationToken token)
    {
        var addresses = await ResolveAsync(endpoint.Host, allowPrivateHosts, token);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

public class BlockedAddressException(IPAddress address) : IOException($"blocked private address {address}");

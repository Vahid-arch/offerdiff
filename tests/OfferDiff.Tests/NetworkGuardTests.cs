using System.Net;
using OfferDiff.Core;

namespace OfferDiff.Tests;

// No real network: IP literals are parsed by Dns without a lookup, and blocked hosts fail before any socket opens.
public class NetworkGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]          // loopback
    [InlineData("127.255.255.254")]
    [InlineData("::1")]
    [InlineData("10.0.0.1")]           // private
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]    // link-local, cloud metadata
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]            // unique-local
    [InlineData("fd12:3456::1")]
    [InlineData("0.0.0.0")]            // unspecified
    [InlineData("::")]
    [InlineData("224.0.0.1")]          // multicast
    [InlineData("ff02::1")]
    [InlineData("100.64.0.1")]         // carrier-grade NAT
    [InlineData("100.127.255.255")]
    [InlineData("::ffff:10.0.0.1")]    // IPv4-mapped IPv6 ::ffff:0:0/96
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("255.255.255.255")]    // broadcast
    [InlineData("64:ff9b::a00:5")]     // NAT64 64:ff9b::/96 -> 10.0.0.5
    [InlineData("64:ff9b::7f00:1")]    // -> 127.0.0.1
    [InlineData("64:ff9b::a9fe:a9fe")] // -> 169.254.169.254
    [InlineData("64:ff9b::c0a8:101")]  // -> 192.168.1.1
    [InlineData("64:ff9b::6440:1")]    // -> 100.64.0.1
    [InlineData("64:ff9b::f000:1")]    // -> 240.0.0.1 (reserved)
    [InlineData("64:ff9b:1::a00:5")]   // NAT64 local-use 64:ff9b:1::/48 -> 10.0.0.5
    [InlineData("64:ff9b:1::a9fe:a9fe")] // -> 169.254.169.254
    [InlineData("2002:a00:5::")]       // 6to4 2002::/16 -> 10.0.0.5
    [InlineData("2002:7f00:1::1")]     // -> 127.0.0.1
    [InlineData("2002:a9fe:a9fe::")]   // -> 169.254.169.254
    [InlineData("2002:c0a8:101:1::1")] // -> 192.168.1.1
    [InlineData("2001:0:4136:e378:8000:63bf:f5ff:fffa")] // Teredo 2001::/32, client ~f5ff:fffa -> 10.0.0.5
    [InlineData("2001:0:4136:e378:8000:63bf:80ff:fffe")] // -> 127.0.0.1
    [InlineData("2001:0:4136:e378:8000:63bf:5601:5601")] // -> 169.254.169.254
    public void Blocks_private_and_internal_addresses(string ip)
    {
        Assert.True(NetworkGuard.IsBlocked(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("11.0.0.1")]           // just outside 10/8
    [InlineData("172.15.255.255")]     // just outside 172.16/12
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]        // just outside 192.168/16
    [InlineData("100.63.255.255")]     // just outside 100.64/10
    [InlineData("100.128.0.1")]
    [InlineData("::ffff:1.1.1.1")]     // IPv4-mapped, public
    [InlineData("64:ff9b::808:808")]   // NAT64 -> 8.8.8.8
    [InlineData("64:ff9b::101:101")]   // -> 1.1.1.1
    [InlineData("64:ff9b:1::808:808")] // NAT64 local-use -> 8.8.8.8
    [InlineData("2002:808:808::1")]    // 6to4 -> 8.8.8.8
    [InlineData("2002:101:101::")]     // -> 1.1.1.1
    [InlineData("2001:0:4136:e378:8000:63bf:f7f7:f7f7")] // Teredo -> 8.8.8.8
    [InlineData("2001:0:4136:e378:8000:63bf:fefe:fefe")] // -> 1.1.1.1
    public void Allows_public_addresses(string ip)
    {
        Assert.False(NetworkGuard.IsBlocked(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("64:ff9b::a00:5", "10.0.0.5")]
    [InlineData("64:ff9b:1::808:808", "8.8.8.8")]
    [InlineData("64:ff9b:1:abcd::c0a8:101", "192.168.1.1")]     // anything inside the /48
    [InlineData("2002:a9fe:a9fe::", "169.254.169.254")]
    [InlineData("2002:808:808:1234::5678", "8.8.8.8")]          // the rest of a 6to4 address is ignored
    [InlineData("2001:0:4136:e378:8000:63bf:f5ff:fffa", "10.0.0.5")]
    [InlineData("2001:0:4136:e378:8000:63bf:f7f7:f7f7", "8.8.8.8")]
    public void Extracts_the_embedded_ipv4(string ipv6, string ipv4)
    {
        Assert.Equal(IPAddress.Parse(ipv4), NetworkGuard.EmbeddedIPv4(IPAddress.Parse(ipv6)));
    }

    [Theory]
    [InlineData("2606:4700:4700::1111")]  // ordinary IPv6
    [InlineData("2001:db8::a00:5")]       // 2001:db8::/32 is documentation, not Teredo 2001::/32
    [InlineData("64:ff9b:0:0:1::a00:5")]  // inside 64:ff9b::/32 but neither /96 NAT64 prefix
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.1")]       // mapped: handled by MapToIPv4, not by this method
    [InlineData("10.0.0.5")]              // already IPv4
    public void Other_addresses_carry_no_embedded_ipv4(string ip)
    {
        Assert.Null(NetworkGuard.EmbeddedIPv4(IPAddress.Parse(ip)));
    }

    [Fact]
    public async Task Resolve_rejects_blocked_address_by_default()
    {
        var ex = await Assert.ThrowsAsync<BlockedAddressException>(() => NetworkGuard.ResolveAsync("127.0.0.1", allowPrivateHosts: false));

        Assert.Equal("blocked private address 127.0.0.1", ex.Message);
    }

    [Fact]
    public async Task Resolve_with_allow_private_hosts_does_not_block()
    {
        var addresses = await NetworkGuard.ResolveAsync("127.0.0.1", allowPrivateHosts: true);

        Assert.Equal([IPAddress.Loopback], addresses);
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/products/a", "blocked private address 127.0.0.1")]
    [InlineData("http://[::1]:9/products/a", "blocked private address ::1")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "blocked private address 169.254.169.254")]
    [InlineData("http://[64:ff9b::a9fe:a9fe]/latest/meta-data/", "blocked private address 64:ff9b::a9fe:a9fe")]
    [InlineData("http://[2002:a00:5::1]/products/a", "blocked private address 2002:a00:5::1")]
    public void Create_blocks_private_addresses_before_connecting(string url, string message)
    {
        var source = HttpPageSource.Create();

        Assert.Equal(message, Assert.Throws<BlockedAddressException>(() => source.GetHtml(url)).Message);
    }

    [Fact]
    public void Blocked_page_becomes_unknown()
    {
        var item = new FeedItem(2, "A1", "Mug", "http://10.0.0.5/products/mug", new Price(12.99m, "USD"), null, "in_stock");

        var result = Comparer.Compare(item, HttpPageSource.Create().GetHtml);

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Equal("page could not be loaded: blocked private address 10.0.0.5", Assert.Single(result.Details));
    }
}

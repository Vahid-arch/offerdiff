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
    [InlineData("::ffff:10.0.0.1")]    // IPv4-mapped IPv6
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("255.255.255.255")]    // broadcast
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
    public void Allows_public_addresses(string ip)
    {
        Assert.False(NetworkGuard.IsBlocked(IPAddress.Parse(ip)));
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

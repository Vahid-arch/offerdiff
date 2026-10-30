using OfferDiff.Core;

namespace OfferDiff.Tests;

public class RobotsTxtTests
{
    static bool Allowed(string robots, string path) => RobotsTxt.Parse(robots).IsAllowed("OfferDiff", path);

    [Fact]
    public void Specific_agent_group_beats_star()
    {
        const string robots = "User-agent: *\nDisallow: /\n\nUser-agent: OfferDiff\nDisallow: /private\n";

        Assert.True(Allowed(robots, "/products/mug"));
        Assert.False(Allowed(robots, "/private/x"));
    }

    [Fact]
    public void Longest_matching_rule_wins()
    {
        const string robots = "User-agent: *\nDisallow: /shop\nAllow: /shop/products\n";

        Assert.True(Allowed(robots, "/shop/products/mug"));
        Assert.False(Allowed(robots, "/shop/cart"));
    }

    [Fact]
    public void Allow_wins_a_tie()
    {
        Assert.True(Allowed("User-agent: *\nDisallow: /page\nAllow: /page\n", "/page"));
    }

    [Theory]
    [InlineData("/a/b.json", false)]
    [InlineData("/a/b.json?x=1", true)]   // "$" anchors the end, so a query breaks the match
    [InlineData("/a/b.jsonx", true)]
    [InlineData("/products?sort=asc", false)]
    [InlineData("/products?page=2", true)]
    public void Star_and_dollar_patterns(string path, bool allowed)
    {
        Assert.Equal(allowed, Allowed("User-agent: *\nDisallow: /*.json$\nDisallow: /*?sort=\n", path));
    }

    [Fact]
    public void Dollar_in_the_middle_is_literal()
    {
        Assert.False(Allowed("User-agent: *\nDisallow: /a$b\n", "/a$b/c"));
    }

    [Fact]
    public void Empty_disallow_allows_everything()
    {
        Assert.True(Allowed("User-agent: *\nDisallow:\n", "/anything"));
    }

    [Fact]
    public void Comments_are_ignored()
    {
        const string robots = "# store rules\nUser-agent: * # everyone\nDisallow: /private # secret\n# Disallow: /\n";

        Assert.False(Allowed(robots, "/private"));
        Assert.True(Allowed(robots, "/products"));
    }

    [Theory]
    [InlineData("offerdiff")]
    [InlineData("OFFERDIFF")]
    [InlineData("OfferDiff/1.0")]
    public void Agent_name_matches_case_insensitively(string agent)
    {
        Assert.False(Allowed($"User-agent: {agent}\nDisallow: /\n", "/products"));
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_one_group()
    {
        const string robots = "User-agent: Googlebot\nUser-agent: OfferDiff\nDisallow: /x\n\nUser-agent: *\nDisallow: /\n";

        Assert.False(Allowed(robots, "/x"));
        Assert.True(Allowed(robots, "/y"));
    }

    [Fact]
    public void Separate_groups_for_the_same_agent_are_combined()
    {
        const string robots = "User-agent: OfferDiff\nDisallow: /a\n\nUser-agent: OfferDiff\nDisallow: /b\n";

        Assert.False(Allowed(robots, "/a"));
        Assert.False(Allowed(robots, "/b"));
    }

    [Fact]
    public void No_matching_group_allows_everything()
    {
        Assert.True(Allowed("User-agent: Googlebot\nDisallow: /\n", "/products"));
        Assert.True(Allowed("", "/products"));
    }

    [Fact]
    public void Unknown_lines_crawl_delay_and_sitemap_are_ignored()
    {
        const string robots = "Sitemap: https://example-store.test/sitemap.xml\nUser-agent: *\nCrawl-delay: 5\nNoindex: /x\nDisallow: /a\r\n";

        Assert.False(Allowed(robots, "/a"));
        Assert.True(Allowed(robots, "/x"));
    }

    [Fact]
    public void Rules_before_any_user_agent_are_ignored()
    {
        Assert.True(Allowed("Disallow: /\nUser-agent: *\nAllow: /\n", "/products"));
        Assert.True(Allowed("Disallow: /\n", "/products"));
    }

    [Fact]
    public void Only_the_first_500_KiB_is_parsed()
    {
        var robots = "# " + new string('x', RobotsTxt.MaxChars) + "\nUser-agent: *\nDisallow: /\n";

        Assert.True(Allowed(robots, "/products"));
    }
}

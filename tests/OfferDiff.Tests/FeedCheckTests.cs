using OfferDiff.Core;

namespace OfferDiff.Tests;

public class FeedCheckTests : IDisposable
{
    readonly string csv = Path.Combine(Path.GetTempPath(), $"offerdiff-{Guid.NewGuid():N}.csv");

    public void Dispose() => File.Delete(csv);

    [Fact]
    public void Sample_feed_end_to_end()
    {
        var report = FeedCheck.Run(Samples.Feed); // default pages folder next to the CSV

        Assert.Empty(report.FeedErrors);
        Assert.Equal(4, report.Count(Verdict.Match));
        Assert.Equal(2, report.Count(Verdict.Mismatch));
        Assert.Equal(2, report.Count(Verdict.Unknown));
        Assert.Equal("8 rows: 4 match, 2 mismatch, 2 unknown", report.Summary);
        Assert.Equal(1, report.ExitCode);
    }

    [Fact]
    public void Custom_page_loader_gives_the_same_verdicts_as_fixture_mode()
    {
        // stands in for a live fetcher: maps each URL to the fixture file by hand, without FixturePageSource
        string FakeGetHtml(string url) =>
            File.ReadAllText(Path.Combine(Samples.Pages, "products", new Uri(url).AbsolutePath.Split('/')[^1] + ".html"));

        var fixture = FeedCheck.Run(Samples.Feed);
        var custom = FeedCheck.Run(Samples.Feed, FakeGetHtml);

        Assert.Equal(fixture.Results.Select(r => (r.Id, r.Verdict, r.MatchedBy)), custom.Results.Select(r => (r.Id, r.Verdict, r.MatchedBy)));
        Assert.Equal(fixture.Summary, custom.Summary);
    }

    [Fact]
    public void Progress_is_reported_before_each_row()
    {
        var calls = new List<string>();

        FeedCheck.Run(Samples.Feed, new FixturePageSource(Samples.Pages).GetHtml, (n, total, id) => calls.Add($"{n}/{total}: {id}"));

        Assert.Equal(8, calls.Count);
        Assert.Equal("1/8: MUG-BLUE", calls[0]);
        Assert.Equal("8/8: LADLE-STEEL", calls[^1]);
    }

    [Fact]
    public void Unknown_and_bad_rows_alone_give_exit_code_0()
    {
        File.WriteAllText(csv, """
            id,title,link,price,sale_price,availability
            MUG-BLUE,Blue Mug,https://example-store.test/products/blue-mug,12.99 USD,,in_stock
            COASTER-OAK,Oak Coaster Set,https://example-store.test/products/oak-coaster,9.50 USD,,in_stock
            BAD,Bad Row,https://example-store.test/products/bad,twelve,,in_stock
            """);

        var report = FeedCheck.Run(csv, Samples.Pages);

        Assert.Equal(4, Assert.Single(report.FeedErrors).Row);
        Assert.Equal("2 rows: 1 match, 0 mismatch, 1 unknown (1 bad feed rows skipped)", report.Summary);
        Assert.Equal(0, report.ExitCode);
    }

    [Theory]
    [InlineData("id;title;link;price;sale_price;availability\nMUG-BLUE;Blue Mug;https://example-store.test/products/blue-mug;12.99 USD;;in_stock\n")]
    [InlineData("id,title,link,price,sale_price,availability\nBAD,Bad Row,https://example-store.test/products/bad,twelve,,in_stock\n")]
    [InlineData("id,title,link,price,sale_price,availability\n")]
    public void No_readable_row_gives_exit_code_2(string content)
    {
        File.WriteAllText(csv, content);

        var report = FeedCheck.Run(csv, Samples.Pages);

        Assert.Empty(report.Results);
        Assert.Equal(2, report.ExitCode);
    }
}

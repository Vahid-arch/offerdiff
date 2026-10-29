using ListingProof.Core;

namespace ListingProof.Tests;

public class FeedCheckTests : IDisposable
{
    static readonly string Samples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples"));
    readonly string csv = Path.Combine(Path.GetTempPath(), $"listingproof-{Guid.NewGuid():N}.csv");

    public void Dispose() => File.Delete(csv);

    [Fact]
    public void Sample_feed_end_to_end()
    {
        var report = FeedCheck.Run(Path.Combine(Samples, "feed.csv")); // default pages folder next to the CSV

        Assert.Empty(report.FeedErrors);
        Assert.Equal(4, report.Count(Verdict.Match));
        Assert.Equal(2, report.Count(Verdict.Mismatch));
        Assert.Equal(2, report.Count(Verdict.Unknown));
        Assert.Equal("8 rows: 4 match, 2 mismatch, 2 unknown", report.Summary);
        Assert.Equal(1, report.ExitCode);
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

        var report = FeedCheck.Run(csv, Path.Combine(Samples, "pages"));

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

        var report = FeedCheck.Run(csv, Path.Combine(Samples, "pages"));

        Assert.Empty(report.Results);
        Assert.Equal(2, report.ExitCode);
    }
}

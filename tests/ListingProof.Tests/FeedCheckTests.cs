using ListingProof.Core;

namespace ListingProof.Tests;

public class FeedCheckTests
{
    static readonly string Samples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples"));

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
        var csv = Path.Combine(Path.GetTempPath(), $"listingproof-{Guid.NewGuid():N}.csv");
        File.WriteAllText(csv, """
            id,title,link,price,sale_price,availability
            MUG-BLUE,Blue Mug,https://example-store.test/products/blue-mug,12.99 USD,,in_stock
            COASTER-OAK,Oak Coaster Set,https://example-store.test/products/oak-coaster,9.50 USD,,in_stock
            BAD,Bad Row,https://example-store.test/products/bad,twelve,,in_stock
            """);
        try
        {
            var report = FeedCheck.Run(csv, Path.Combine(Samples, "pages"));

            Assert.Equal(4, Assert.Single(report.FeedErrors).Row);
            Assert.Equal("2 rows: 1 match, 0 mismatch, 1 unknown (1 bad feed rows skipped)", report.Summary);
            Assert.Equal(0, report.ExitCode);
        }
        finally
        {
            File.Delete(csv);
        }
    }
}

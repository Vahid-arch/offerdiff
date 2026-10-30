using OfferDiff.Core;

namespace OfferDiff.Tests;

// Keeps the public demo store (docs\demo on GitHub Pages) and samples\live-demo-feed.csv in step with the README.
// Pages are read from disk exactly as GitHub Pages serves them, so this never touches the network.
public class DemoStoreTests
{
    const string SiteRoot = "https://vahid-arch.github.io/offerdiff/";

    // GitHub Pages serves docs\<path> at <SiteRoot><path> and ignores the query string (?variant=N).
    static string FromDocs(string url)
    {
        Assert.StartsWith(SiteRoot, url);
        return File.ReadAllText(Path.Combine(Samples.Docs, new Uri(url).AbsolutePath["/offerdiff/".Length..]));
    }

    [Fact]
    public void Demo_feed_gives_the_verdicts_documented_in_the_README()
    {
        var report = FeedCheck.Run(Samples.DemoFeed, FromDocs);

        Assert.Empty(report.FeedErrors);
        // every row is matched by url, which also proves each JSON-LD offer url is the exact published URL
        Assert.Equal(new Dictionary<string, (Verdict, string?)>
        {
            ["MUG-SAND"] = (Verdict.Match, "url"),
            ["TRAY-PALM"] = (Verdict.Mismatch, "url"),
            ["LAMP-DUNE"] = (Verdict.Match, "url"),
            ["CANDLE-OUD"] = (Verdict.Match, "url"),
            ["CUSHION-FALAJ-S"] = (Verdict.Match, "url"),
            ["CUSHION-FALAJ-L"] = (Verdict.Match, "url"),
        }, report.Results.ToDictionary(r => r.Id, r => (r.Verdict, r.MatchedBy)));
        Assert.Equal(["price: feed 79.00, page 89.00"], report.Results.Single(r => r.Id == "TRAY-PALM").Details);
        Assert.Equal(1, report.ExitCode);

        Assert.Equal("6 rows: 5 match, 1 mismatch, 0 unknown", report.Summary);
        Assert.Contains(report.Summary, File.ReadAllText(Path.Combine(Samples.RepoRoot, "README.md")));
    }

    [Fact]
    public void Index_links_every_product_page()
    {
        var index = File.ReadAllText(Path.Combine(Samples.Docs, "demo", "index.html"));

        foreach (var page in Directory.GetFiles(Path.Combine(Samples.Docs, "demo", "products"), "*.html"))
            Assert.Contains($"href=\"products/{Path.GetFileName(page)}\"", index);
    }
}

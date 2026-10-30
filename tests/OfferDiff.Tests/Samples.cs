namespace OfferDiff.Tests;

// Tests run from tests/OfferDiff.Tests/bin/<config>/<tfm>/, five folders below the repo root.
static class Samples
{
    public static readonly string Folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples"));
    public static readonly string Feed = Path.Combine(Folder, "feed.csv");
    public static readonly string Pages = Path.Combine(Folder, "pages");
    public static readonly string DemoFeed = Path.Combine(Folder, "live-demo-feed.csv");
    public static readonly string RepoRoot = Path.GetDirectoryName(Folder)!;
    public static readonly string Docs = Path.Combine(RepoRoot, "docs");
}

namespace ListingProof.Tests;

// Tests run from tests/ListingProof.Tests/bin/<config>/<tfm>/, five folders below the repo root.
static class Samples
{
    public static readonly string Folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples"));
    public static readonly string Feed = Path.Combine(Folder, "feed.csv");
    public static readonly string Pages = Path.Combine(Folder, "pages");
}

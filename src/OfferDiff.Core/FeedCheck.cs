namespace OfferDiff.Core;

public record CheckReport(List<FeedError> FeedErrors, List<ComparisonResult> Results)
{
    public int Count(Verdict verdict) => Results.Count(r => r.Verdict == verdict);

    // No readable row (wrong delimiter, missing columns, empty feed) means nothing was checked: an error, not a pass.
    // Unknown alone does not fail: it means "could not check", not "wrong".
    public int ExitCode => Results.Count == 0 ? 2 : Count(Verdict.Mismatch) > 0 ? 1 : 0;

    public string Summary =>
        $"{Results.Count} rows: {Count(Verdict.Match)} match, {Count(Verdict.Mismatch)} mismatch, {Count(Verdict.Unknown)} unknown" +
        (FeedErrors.Count > 0 ? $" ({FeedErrors.Count} bad feed rows skipped)" : "");
}

public static class FeedCheck
{
    // Default pages folder is "pages" next to the CSV, so the sample works from any current directory.
    public static string DefaultPagesFolder(string csvPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(csvPath))!, "pages");

    // Fixture mode: pages come from local files.
    public static CheckReport Run(string csvPath, string? pagesFolder = null) =>
        Run(csvPath, new FixturePageSource(pagesFolder ?? DefaultPagesFolder(csvPath)).GetHtml);

    // getHtml loads one page by URL (FixturePageSource.GetHtml or HttpPageSource.GetHtml).
    // progress, if given, is called before each row with (row number, total rows, id).
    public static CheckReport Run(string csvPath, Func<string, string> getHtml, Action<int, int, string>? progress = null)
    {
        var feed = FeedReader.Read(csvPath);
        var results = new List<ComparisonResult>();
        foreach (var item in feed.Items)
        {
            progress?.Invoke(results.Count + 1, feed.Items.Count, item.Id);
            results.Add(Comparer.Compare(item, getHtml));
        }
        return new CheckReport(feed.Errors, results);
    }
}

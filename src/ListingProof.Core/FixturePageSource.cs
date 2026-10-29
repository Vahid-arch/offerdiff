namespace ListingProof.Core;

// Stands in for an HTTP fetcher: same input (a URL), same output (the page HTML), but read from disk.
public class FixturePageSource(string pagesFolder)
{
    public const string Host = "example-store.test";

    readonly string root = Path.GetFullPath(pagesFolder) + Path.DirectorySeparatorChar;

    public string GetHtml(string url) => File.ReadAllText(ResolvePath(url));

    // https://example-store.test/products/blue-mug?variant=2 -> <pagesFolder>\products\blue-mug.html
    public string ResolvePath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new ArgumentException($"not an http(s) URL: {url}");
        if (!uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"host must be {Host}: {url}");

        // Uri already collapses "/a/../b", so check the raw text too: a URL with ".." is rejected, not silently rewritten.
        var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        if (path == "" || url.Contains("..") || Uri.UnescapeDataString(url).Contains(".."))
            throw new ArgumentException($"bad page path: {url}");

        var file = Path.GetFullPath(Path.Combine(root, path + ".html"));
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"path escapes pages folder: {url}");
        return file;
    }
}

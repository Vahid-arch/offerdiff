using ListingProof.Core;

namespace ListingProof.Tests;

public class FixturePageSourceTests
{
    static readonly string Pages = Path.GetFullPath("pages");
    readonly FixturePageSource source = new(Pages);

    [Theory]
    [InlineData("https://example-store.test/products/blue-mug")]
    [InlineData("https://example-store.test/products/blue-mug/")]
    [InlineData("https://EXAMPLE-STORE.test/products/blue-mug?variant=2#top")]
    public void Maps_url_to_html_file_ignoring_query(string url)
    {
        Assert.Equal(Path.Combine(Pages, "products", "blue-mug.html"), source.ResolvePath(url));
    }

    [Theory]
    [InlineData("https://example-store.test/products/v2..final", "v2..final.html")]
    [InlineData("https://example-store.test/products/mug?back=../list", "mug.html")]
    public void Dots_inside_a_segment_or_the_query_are_allowed(string url, string file)
    {
        Assert.Equal(Path.Combine(Pages, "products", file), source.ResolvePath(url));
    }

    [Theory]
    [InlineData("https://evil.test/products/blue-mug")]
    [InlineData("https://example-store.test.evil.test/products/blue-mug")]
    [InlineData("ftp://example-store.test/products/blue-mug")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("products/blue-mug")]
    [InlineData("https://example-store.test/")]
    [InlineData("https://example-store.test/../secret")]
    [InlineData("https://example-store.test/products/../../secret")]
    [InlineData("https://example-store.test/products/%2e%2e/%2e%2e/secret")]
    [InlineData("https://example-store.test/products/..%5C..%5Csecret")]
    public void Rejects_other_hosts_and_paths_that_escape(string url)
    {
        Assert.Throws<ArgumentException>(() => source.ResolvePath(url));
    }

    [Fact]
    public void Rejects_windows_drive_path_on_windows()
    {
        // on Linux "\" is an ordinary file-name character, so this URL stays inside the pages folder there
        if (!OperatingSystem.IsWindows()) return;

        Assert.Throws<ArgumentException>(() => source.ResolvePath("https://example-store.test/C:%5CWindows%5Cwin"));
    }
}

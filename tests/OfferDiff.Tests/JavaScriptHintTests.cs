using OfferDiff.Core;

namespace OfferDiff.Tests;

// Small synthetic page shells: what the server sends before any JavaScript runs.
public class JavaScriptHintTests
{
    const string AngularShell = """
        <!doctype html>
        <html><head><title>Example Store</title><script src="main.js" type="module"></script></head>
        <body><app-root ng-version="19.0.0"></app-root></body></html>
        """;

    const string ReactShell = """
        <!doctype html>
        <html><head><title>Example Store</title></head>
        <body><noscript>You need to enable JavaScript to run this app.</noscript><div id="root"></div>
        <script src="/static/js/main.js"></script></body></html>
        """;

    const string VueShell = """<html><body><div id='app'></div><script src="/app.js"></script></body></html>""";

    const string NextJsPage = """
        <html><body><div id="__next"><h1>Blue Mug</h1><p>A sturdy blue mug for everyday coffee.</p></div>
        <script id="__NEXT_DATA__" type="application/json">{"props":{}}</script></body></html>
        """;

    const string AngularSsrPage = """
        <html><head><style data-beasties-container>.x{}</style></head>
        <body><h1>Blue Mug</h1><p>A sturdy blue mug for everyday coffee.</p></body></html>
        """;

    const string EmptyBody = """<html><head><script src="/bundle.js"></script></head><body>  </body></html>""";

    const string NormalPage = """
        <!doctype html>
        <html><head><title>Blue Mug - Example Store</title></head>
        <body><h1>Blue Mug</h1><p>$12.99</p><p>A sturdy blue mug for everyday coffee.</p></body></html>
        """;

    [Theory]
    [InlineData(AngularShell)]
    [InlineData(ReactShell)]
    [InlineData(VueShell)]
    [InlineData(NextJsPage)]
    [InlineData(AngularSsrPage)]
    [InlineData(EmptyBody)]
    public void App_shell_without_json_ld_gets_the_hint(string html)
    {
        Assert.Equal(["no JSON-LD block on page", JsonLdExtractor.JavaScriptHint], JsonLdExtractor.Extract(html).Errors);
    }

    [Fact]
    public void Normal_page_without_json_ld_gets_no_hint()
    {
        Assert.Equal(["no JSON-LD block on page"], JsonLdExtractor.Extract(NormalPage).Errors);
    }

    [Fact]
    public void Sample_page_without_json_ld_gets_no_hint()
    {
        var html = File.ReadAllText(Path.Combine(Samples.Pages, "products", "oak-coaster.html"));

        Assert.Equal(["no JSON-LD block on page"], JsonLdExtractor.Extract(html).Errors);
    }

    [Fact]
    public void App_with_json_ld_gets_no_hint()
    {
        const string html = """
            <html><body><app-root></app-root>
            <script type="application/ld+json">{"@type":"Product","offers":{"@type":"Offer","price":5,"priceCurrency":"USD"}}</script>
            </body></html>
            """;

        Assert.Empty(JsonLdExtractor.Extract(html).Errors);
    }

    [Fact]
    public void Hint_keeps_the_verdict_unknown()
    {
        var item = new FeedItem(2, "MUG-BLUE", "Blue Mug", "https://example-store.test/products/blue-mug", new Price(12.99m, "USD"), null, "in_stock");

        var result = Comparer.Compare(item, _ => AngularShell);

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Equal(["no Product offer on page", "no JSON-LD block on page", JsonLdExtractor.JavaScriptHint], result.Details);
    }
}

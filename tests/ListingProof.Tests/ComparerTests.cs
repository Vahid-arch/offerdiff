using ListingProof.Core;

namespace ListingProof.Tests;

public class ComparerTests
{
    const string Link = "https://example-store.test/products/mug?variant=2";

    static FeedItem Item(decimal price = 12.99m, decimal? sale = null, string availability = "in_stock", string link = Link) =>
        new(2, "A1", "Mug", link, new Price(price, "USD"), sale is { } s ? new Price(s, "USD") : null, availability);

    static PageOffer Offer(decimal? price = 12.99m, string? currency = "USD", string? availability = "in_stock", string? url = Link, string? sku = null) =>
        new(sku, price, currency, availability, url);

    static ComparisonResult Compare(FeedItem item, params PageOffer[] offers) =>
        Comparer.Compare(item, new JsonLdResult([.. offers], []));

    [Fact]
    public void Match_by_url()
    {
        var result = Compare(Item(), Offer());

        Assert.Equal(Verdict.Match, result.Verdict);
        Assert.Equal("url", result.MatchedBy);
    }

    [Fact]
    public void Url_scheme_and_host_ignore_case_but_query_must_be_exact()
    {
        var result = Compare(Item(),
            Offer(price: 1m, url: "https://example-store.test/products/mug?variant=3"),
            Offer(url: "HTTPS://Example-Store.TEST/products/mug?variant=2"));

        Assert.Equal(Verdict.Match, result.Verdict);
        Assert.Equal("url", result.MatchedBy);
    }

    [Fact]
    public void Match_by_sku_when_no_url_matches()
    {
        var result = Compare(Item(),
            Offer(price: 1m, url: null, sku: "OTHER"),
            Offer(url: null, sku: "A1"));

        Assert.Equal(Verdict.Match, result.Verdict);
        Assert.Equal("sku", result.MatchedBy);
    }

    [Fact]
    public void Single_offer_is_used_when_nothing_else_matches()
    {
        var result = Compare(Item(), Offer(url: "https://example-store.test/elsewhere"));

        Assert.Equal(Verdict.Match, result.Verdict);
        Assert.Equal("single offer", result.MatchedBy);
    }

    [Fact]
    public void Ambiguous_variants_are_unknown_never_mismatch()
    {
        var result = Compare(Item(),
            Offer(price: 27m, url: "https://example-store.test/products/mug?variant=1", sku: "A1-S"),
            Offer(price: 31m, url: "https://example-store.test/products/mug?variant=3", sku: "A1-L"));

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Null(result.MatchedBy);
        Assert.StartsWith("ambiguous variants", Assert.Single(result.Details));
    }

    [Fact]
    public void Conflicting_offers_at_same_rule_are_unknown()
    {
        var result = Compare(Item(), Offer(price: 12.99m), Offer(price: 15.00m));

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Equal("url", result.MatchedBy);
        Assert.StartsWith("conflicting structured data", result.Details[0]);
    }

    [Fact]
    public void Identical_duplicate_offers_are_not_a_conflict()
    {
        Assert.Equal(Verdict.Match, Compare(Item(), Offer(), Offer()).Verdict);
    }

    [Fact]
    public void Mismatch_lists_every_differing_field()
    {
        var result = Compare(Item(), Offer(price: 39.00m, currency: "EUR", availability: "out_of_stock"));

        Assert.Equal(Verdict.Mismatch, result.Verdict);
        Assert.Equal(
            ["price: feed 12.99, page 39.00", "currency: feed USD, page EUR", "availability: feed in_stock, page out_of_stock"],
            result.Details);
    }

    [Fact]
    public void Sale_price_is_used_when_present()
    {
        Assert.Equal(Verdict.Match, Compare(Item(price: 18.00m, sale: 14.40m), Offer(price: 14.40m)).Verdict);

        var regular = Compare(Item(price: 18.00m, sale: 14.40m), Offer(price: 18.00m));
        Assert.Equal(Verdict.Mismatch, regular.Verdict);
        Assert.Equal("price: feed 14.40, page 18.00", Assert.Single(regular.Details));
    }

    [Theory]
    [InlineData(13.00, Verdict.Match)]    // 0.01 off: within tolerance
    [InlineData(12.98, Verdict.Match)]
    [InlineData(13.01, Verdict.Mismatch)] // 0.02 off
    public void Price_tolerance_is_one_cent(decimal pagePrice, Verdict expected)
    {
        Assert.Equal(expected, Compare(Item(price: 12.99m), Offer(price: pagePrice)).Verdict);
    }

    [Fact]
    public void Missing_page_fields_are_unknown_with_each_reason()
    {
        var result = Compare(Item(), Offer(price: null, currency: null, availability: null));

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Equal(["page price missing", "page currency missing", "page availability missing"], result.Details);
    }

    [Fact]
    public void Page_without_offers_is_unknown_with_extractor_errors()
    {
        var result = Comparer.Compare(Item(), new JsonLdResult([], ["JSON-LD block 1: invalid JSON (x)"]));

        Assert.Equal(Verdict.Unknown, result.Verdict);
        Assert.Equal(["no Product offer on page", "JSON-LD block 1: invalid JSON (x)"], result.Details);
    }

    [Fact]
    public void Page_that_cannot_be_loaded_is_unknown()
    {
        var pages = new FixturePageSource(Path.GetFullPath("no-such-folder"));

        var missingFile = Comparer.Compare(Item(), pages.GetHtml);
        var rejectedUrl = Comparer.Compare(Item(link: "https://evil.test/products/mug"), pages.GetHtml);

        Assert.Equal(Verdict.Unknown, missingFile.Verdict);
        Assert.StartsWith("page could not be loaded", Assert.Single(missingFile.Details));
        Assert.Equal(Verdict.Unknown, rejectedUrl.Verdict);
        Assert.Contains("host must be example-store.test", Assert.Single(rejectedUrl.Details));
    }

    [Fact]
    public void Sample_feed_gives_expected_verdict_per_row()
    {
        var samples = Path.Combine(AppContext.BaseDirectory, "../../../../../samples");
        var pages = new FixturePageSource(Path.Combine(samples, "pages"));
        var feed = FeedReader.Read(Path.Combine(samples, "feed.csv"));

        var verdicts = feed.Items.Select(i => Comparer.Compare(i, pages.GetHtml)).ToDictionary(r => r.Id, r => r.Verdict);

        Assert.Equal(new Dictionary<string, Verdict>
        {
            ["MUG-BLUE"] = Verdict.Match,
            ["KETTLE-RED"] = Verdict.Mismatch,
            ["TEAPOT-GREEN"] = Verdict.Mismatch,
            ["BOWL-LARGE"] = Verdict.Match,
            ["BOARD-WALNUT"] = Verdict.Match,
            ["APRON-LINEN-M"] = Verdict.Match,
            ["COASTER-OAK"] = Verdict.Unknown,
            ["LADLE-STEEL"] = Verdict.Unknown,
        }, verdicts);
    }
}

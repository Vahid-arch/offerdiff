using ListingProof.Core;

namespace ListingProof.Tests;

public class JsonLdExtractorTests
{
    static readonly FixturePageSource Pages = new(Path.Combine(AppContext.BaseDirectory, "../../../../../samples/pages"));
    const string Store = "https://example-store.test/products/";

    static JsonLdResult ExtractFixture(string slug) => JsonLdExtractor.Extract(Pages.GetHtml(Store + slug));

    [Fact]
    public void Single_product_with_offer()
    {
        var result = ExtractFixture("blue-mug");

        Assert.Empty(result.Errors);
        Assert.Equal(new PageOffer("MUG-BLUE", 12.99m, "USD", "in_stock", Store + "blue-mug"), Assert.Single(result.Offers));
    }

    [Fact]
    public void Offers_array_with_string_price()
    {
        var result = ExtractFixture("red-kettle");

        Assert.Empty(result.Errors);
        Assert.Equal(new PageOffer("KETTLE-RED", 39.00m, "USD", "in_stock", Store + "red-kettle"), Assert.Single(result.Offers));
    }

    [Fact]
    public void Graph_wrapper()
    {
        var result = ExtractFixture("green-teapot");

        Assert.Empty(result.Errors);
        Assert.Equal(new PageOffer("TEAPOT-GREEN", 24.00m, "EUR", "in_stock", Store + "green-teapot"), Assert.Single(result.Offers));
    }

    [Fact]
    public void Two_blocks_only_product_counts()
    {
        var result = ExtractFixture("ceramic-bowl");

        Assert.Empty(result.Errors);
        Assert.Equal(new PageOffer("BOWL-LARGE", 14.40m, "USD", "in_stock", Store + "ceramic-bowl"), Assert.Single(result.Offers));
    }

    [Fact]
    public void Aggregate_offer_with_single_price()
    {
        var result = ExtractFixture("walnut-board");

        Assert.Empty(result.Errors);
        Assert.Equal(new PageOffer("BOARD-WALNUT", 45.00m, "USD", "out_of_stock", Store + "walnut-board"), Assert.Single(result.Offers));
    }

    [Fact]
    public void Product_group_returns_one_offer_per_variant()
    {
        var result = ExtractFixture("linen-apron");

        Assert.Empty(result.Errors);
        Assert.Equal(
            [
                new PageOffer("APRON-LINEN-S", 27.00m, "USD", "in_stock", Store + "linen-apron?variant=1"),
                new PageOffer("APRON-LINEN-M", 29.00m, "USD", "preorder", Store + "linen-apron?variant=2"),
                new PageOffer("APRON-LINEN-L", 31.00m, "USD", "backorder", Store + "linen-apron?variant=3"),
            ],
            result.Offers);
    }

    [Fact]
    public void No_json_ld_is_reported()
    {
        var result = ExtractFixture("oak-coaster");

        Assert.Empty(result.Offers);
        Assert.Equal("no JSON-LD block on page", Assert.Single(result.Errors));
    }

    [Fact]
    public void Invalid_json_is_reported_not_thrown()
    {
        var result = ExtractFixture("steel-ladle");

        Assert.Empty(result.Offers);
        Assert.StartsWith("JSON-LD block 1: invalid JSON", Assert.Single(result.Errors));
    }

    [Fact]
    public void Aggregate_offer_price_range_has_no_price()
    {
        var html = """
            <script type="application/ld+json">
            {"@type":"Product","sku":"X","offers":{"@type":"AggregateOffer","lowPrice":10,"highPrice":20,"priceCurrency":"USD"}}
            </script>
            """;

        Assert.Null(Assert.Single(JsonLdExtractor.Extract(html).Offers).Price);
    }

    [Fact]
    public void Odd_shapes_do_not_throw()
    {
        var html = """
            <script type="application/ld+json">[1, "x", null, {"@type": 5}, {"@type":"Product","offers":"free"}]</script>
            <script type="application/ld+json">{"@type":["Product","Thing"],"offers":{"@type":"Offer","price":{"v":1},"availability":"Maybe"}}</script>
            """;

        var result = JsonLdExtractor.Extract(html);

        var offer = Assert.Single(result.Offers);
        Assert.Null(offer.Price);
        Assert.Null(offer.Availability);
        Assert.Equal("unrecognized availability 'Maybe'", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("https://schema.org/InStock", "in_stock")]
    [InlineData("http://schema.org/InStock", "in_stock")]
    [InlineData("InStock", "in_stock")]
    [InlineData("https://schema.org/OutOfStock", "out_of_stock")]
    [InlineData("PreOrder", "preorder")]
    [InlineData("http://schema.org/BackOrder", "backorder")]
    [InlineData("https://schema.org/Discontinued", null)]
    [InlineData(null, null)]
    public void Maps_schema_org_availability(string? value, string? expected)
    {
        Assert.Equal(expected, JsonLdExtractor.MapAvailability(value));
    }
}

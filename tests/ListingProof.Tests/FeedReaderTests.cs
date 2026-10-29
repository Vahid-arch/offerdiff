using ListingProof.Core;

namespace ListingProof.Tests;

public class FeedReaderTests
{
    const string Header = "id,title,link,price,sale_price,availability\n";

    [Fact]
    public void Reads_valid_rows()
    {
        var result = FeedReader.Parse(Header +
            "A1,Blue Mug,https://example-store.test/products/a,12.99 USD,,in_stock\n" +
            " B2 , Red Kettle ,https://example-store.test/products/b, 34.50 EUR ,30.00 EUR,out_of_stock\n");

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new FeedItem(2, "A1", "Blue Mug", "https://example-store.test/products/a", new Price(12.99m, "USD"), null, "in_stock"), result.Items[0]);
        Assert.Equal("B2", result.Items[1].Id); // whitespace trimmed
        Assert.Equal(new Price(34.50m, "EUR"), result.Items[1].Price);
        Assert.Equal(new Price(30.00m, "EUR"), result.Items[1].SalePrice);
    }

    [Fact]
    public void Quoted_field_can_contain_commas_and_quotes()
    {
        var result = FeedReader.Parse(Header + "A1,\"Bowl, Large \"\"XL\"\"\",https://example-store.test/products/a,12.99 USD,,in_stock\n");

        Assert.Empty(result.Errors);
        Assert.Equal("Bowl, Large \"XL\"", result.Items[0].Title);
    }

    [Fact]
    public void Empty_sale_price_is_null()
    {
        var result = FeedReader.Parse(Header + "A1,Mug,https://example-store.test/products/a,12.99 USD,,in_stock\n");

        Assert.Null(result.Items[0].SalePrice);
    }

    [Theory]
    [InlineData("\"12,99 USD\"")] // quoted, otherwise the comma splits the field
    [InlineData("12.99")]
    [InlineData("USD 12.99")]
    [InlineData("12.99 usd")]
    [InlineData("-5.00 USD")]
    public void Malformed_price_is_reported_with_row_number(string price)
    {
        var result = FeedReader.Parse(Header +
            "A1,Mug,https://example-store.test/products/a,12.99 USD,,in_stock\n" +
            $"B2,Kettle,https://example-store.test/products/b,{price},,in_stock\n");

        Assert.Single(result.Items);
        var error = Assert.Single(result.Errors);
        Assert.Equal(3, error.Row);
        Assert.Contains("bad price", error.Message);
    }

    [Fact]
    public void Bad_availability_and_wrong_field_count_are_reported()
    {
        var result = FeedReader.Parse(Header +
            "A1,Mug,https://example-store.test/products/a,12.99 USD,,in stock\n" +
            "B2,Kettle,12.99 USD,,in_stock\n");

        Assert.Empty(result.Items);
        Assert.Equal([2, 3], result.Errors.Select(e => e.Row));
    }

    [Fact]
    public void Missing_column_is_reported()
    {
        var result = FeedReader.Parse("id,title,link,price\nA1,Mug,https://example-store.test/products/a,12.99 USD\n");

        Assert.Empty(result.Items);
        Assert.Contains("sale_price", Assert.Single(result.Errors).Message);
    }
}

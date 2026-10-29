using System.Globalization;

namespace ListingProof.Core;

public enum Verdict { Match, Mismatch, Unknown }

// MatchedBy is "url", "sku", "single offer", or null when no offer was picked.
public record ComparisonResult(string Id, Verdict Verdict, List<string> Details, string? MatchedBy);

public static class Comparer
{
    // getHtml is FixturePageSource.GetHtml today; an HTTP fetcher with the same shape can replace it.
    public static ComparisonResult Compare(FeedItem item, Func<string, string> getHtml)
    {
        string html;
        try
        {
            html = getHtml(item.Link);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Unknown(item, null, $"page could not be loaded: {ex.Message}");
        }
        return Compare(item, JsonLdExtractor.Extract(html));
    }

    public static ComparisonResult Compare(FeedItem item, JsonLdResult page)
    {
        if (page.Offers.Count == 0)
            return Unknown(item, null, ["no Product offer on page", .. page.Errors]);

        var (offers, rule) = Pick(item, page.Offers);
        if (offers.Count == 0)
            return Unknown(item, null, $"ambiguous variants: {page.Offers.Count} offers on page, none has url {item.Link} or sku {item.Id}");

        if (offers.Select(o => (o.Price, o.Currency, o.Availability)).Distinct().Count() > 1)
            return Unknown(item, rule, [$"conflicting structured data: {offers.Count} offers matched by {rule} disagree", .. offers.Select(Describe)]);

        var offer = offers[0];
        var missing = new List<string>();
        if (offer.Price == null) missing.Add("page price missing");
        if (offer.Currency == null) missing.Add("page currency missing");
        if (offer.Availability == null) missing.Add("page availability missing");
        if (missing.Count > 0) return Unknown(item, rule, [.. missing, .. page.Errors]);

        var feedPrice = item.SalePrice ?? item.Price;
        var pagePrice = new Price(offer.Price!.Value, offer.Currency!);
        var details = new List<string>();
        // decimal is exact and 12.99 == 12.990, so no tolerance is needed
        if (feedPrice.Amount != pagePrice.Amount)
            details.Add($"price: feed {Amount(feedPrice)}, page {Amount(pagePrice)}");
        if (!feedPrice.Currency.Equals(pagePrice.Currency, StringComparison.OrdinalIgnoreCase))
            details.Add($"currency: feed {feedPrice.Currency}, page {pagePrice.Currency}");
        if (item.Availability != offer.Availability)
            details.Add($"availability: feed {item.Availability}, page {offer.Availability}");

        return details.Count > 0
            ? new ComparisonResult(item.Id, Verdict.Mismatch, details, rule)
            : new ComparisonResult(item.Id, Verdict.Match, [$"feed and page agree: {feedPrice}, {item.Availability}"], rule);
    }

    static (List<PageOffer> Offers, string? Rule) Pick(FeedItem item, List<PageOffer> offers)
    {
        var byUrl = offers.Where(o => SameUrl(o.Url, item.Link)).ToList();
        if (byUrl.Count > 0) return (byUrl, "url");

        var bySku = offers.Where(o => o.Sku == item.Id).ToList();
        if (bySku.Count > 0) return (bySku, "sku");

        return offers.Count == 1 ? (offers, "single offer") : ([], null);
    }

    // Scheme and host are case-insensitive (Uri lowercases them); path and query must match exactly.
    static bool SameUrl(string? a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub) &&
        Uri.Compare(ua, ub, UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped, StringComparison.Ordinal) == 0;

    // Amount only, so "12.99 USD" vs "39.00 USD" reads cleanly; currency gets its own detail line.
    static string Amount(Price p) => p.Amount.ToString(CultureInfo.InvariantCulture);

    static string Describe(PageOffer o) => $"offer {o.Url ?? o.Sku ?? "?"}: {o.Price?.ToString(CultureInfo.InvariantCulture) ?? "?"} {o.Currency ?? "?"}, {o.Availability ?? "?"}";

    static ComparisonResult Unknown(FeedItem item, string? rule, params List<string> details) =>
        new(item.Id, Verdict.Unknown, details, rule);
}

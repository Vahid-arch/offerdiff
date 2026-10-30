using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OfferDiff.Core;

public record PageOffer(string? Sku, decimal? Price, string? Currency, string? Availability, string? Url);

public record JsonLdResult(List<PageOffer> Offers, List<string> Errors);

public static class JsonLdExtractor
{
    // shortcut: regex HTML scanning, replace with AngleSharp when real pages are supported
    static readonly Regex ScriptBlock = new(
        """<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public const string JavaScriptHint =
        "structured data may be added by JavaScript; OfferDiff reads server HTML only (see README: JavaScript-rendered pages)";

    // Signs of a JavaScript app shell (Angular, React, Vue, Next.js, Angular SSR's beasties): same regex shortcut as above.
    static readonly Regex AppShellMarker = new(
        """<app-root\b|\bng-version=|<div\b[^>]*\bid\s*=\s*["']?(root|app)\b|__NEXT_DATA__|data-beasties-container""",
        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    static readonly Regex Body = new("""<body\b[^>]*>(.*?)(</body>|$)""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    static readonly Regex NonText = new("""<(script|style|noscript|template)\b.*?</\1\s*>|<[^>]*>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    public static JsonLdResult Extract(string html)
    {
        var result = new JsonLdResult([], []);
        var blocks = ScriptBlock.Matches(html);
        if (blocks.Count == 0)
        {
            result.Errors.Add("no JSON-LD block on page");
            if (LooksLikeJavaScriptApp(html)) result.Errors.Add(JavaScriptHint);
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            try
            {
                using var doc = JsonDocument.Parse(blocks[i].Groups[1].Value);
                Visit(doc.RootElement, null, null, result);
            }
            catch (JsonException ex)
            {
                // ex.LineNumber is 0-based inside the block; add the block's start so it points at the page line
                var line = html[..blocks[i].Groups[1].Index].Count(c => c == '\n') + 1 + (ex.LineNumber ?? 0);
                var reason = ex.Message.Split([" Path:", " LineNumber:", ". "], StringSplitOptions.None)[0].TrimEnd('.');
                result.Errors.Add($"JSON-LD block {i + 1}: invalid JSON (line {line}: {reason})");
            }
        }
        return result;
    }

    // A hint only, never a verdict: framework markers, or a body with almost no visible text.
    static bool LooksLikeJavaScriptApp(string html)
    {
        try
        {
            if (AppShellMarker.IsMatch(html)) return true;
            var body = Body.Match(html);
            if (!body.Success) return false; // no <body> at all: too little to tell
            var text = System.Net.WebUtility.HtmlDecode(NonText.Replace(body.Groups[1].Value, " "));
            // real product pages show a name, a price and a description; an app shell shows nothing (or a noscript note)
            return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 3;
        }
        catch (RegexMatchTimeoutException)
        {
            return false; // a huge or hostile page: skip the hint rather than stall the run
        }
    }

    // "https://schema.org/InStock", "http://schema.org/InStock" or "InStock" -> "in_stock". Unknown -> null.
    public static string? MapAvailability(string? value)
    {
        var name = value?.Trim() ?? "";
        foreach (var prefix in new[] { "https://schema.org/", "http://schema.org/" })
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) name = name[prefix.Length..];

        return name.ToLowerInvariant() switch
        {
            "instock" => "in_stock",
            "outofstock" => "out_of_stock",
            "preorder" => "preorder",
            "backorder" => "backorder",
            _ => null,
        };
    }

    // sku and url come from the enclosing Product, so an Offer without its own still gets them.
    // inOffers is true inside a Product's "offers", where an object without @type but with a price counts as an Offer.
    static void Visit(JsonElement node, string? sku, string? url, JsonLdResult result, bool inOffers = false)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray()) Visit(item, sku, url, result, inOffers);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;

        if (node.TryGetProperty("@graph", out var graph)) Visit(graph, sku, url, result);
        if (node.TryGetProperty("mainEntity", out var mainEntity)) Visit(mainEntity, sku, url, result);

        if (IsType(node, "Product") || IsType(node, "ProductGroup"))
        {
            sku = Text(node, "sku") ?? sku;
            url = Text(node, "url") ?? url;
            if (node.TryGetProperty("offers", out var offers)) Visit(offers, sku, url, result, inOffers: true);
            // variants do not inherit the group's sku/url, or every variant would look like the same product
            if (node.TryGetProperty("hasVariant", out var variants)) Visit(variants, null, null, result);
        }
        else if (IsType(node, "AggregateOffer") && node.TryGetProperty("offers", out var inner))
        {
            Visit(inner, sku, url, result, inOffers: true);
        }
        else if (IsType(node, "AggregateOffer"))
        {
            var low = ReadPrice(node, "lowPrice", result);
            var high = ReadPrice(node, "highPrice", result) ?? low;
            // a price range has no single price to compare, so leave it empty rather than pick one
            if (low != null && low != high)
                result.Errors.Add(string.Create(CultureInfo.InvariantCulture, $"price range {low}-{high}"));
            AddOffer(node, low == high ? low : null, sku, url, result);
        }
        else if (IsType(node, "Offer") || (inOffers && !node.TryGetProperty("@type", out _) && node.TryGetProperty("price", out _)))
        {
            AddOffer(node, ReadPrice(node, "price", result), sku, url, result);
        }
    }

    static void AddOffer(JsonElement node, decimal? price, string? sku, string? url, JsonLdResult result)
    {
        var rawAvailability = Text(node, "availability");
        var availability = MapAvailability(rawAvailability);
        if (rawAvailability != null && availability == null)
            result.Errors.Add($"unrecognized availability '{rawAvailability}'");

        result.Offers.Add(new PageOffer(
            Text(node, "sku") ?? sku, price, Text(node, "priceCurrency"), availability, Text(node, "url") ?? url));
    }

    static bool IsType(JsonElement node, string type) =>
        node.TryGetProperty("@type", out var t) && t.ValueKind switch
        {
            JsonValueKind.String => t.GetString() == type,
            JsonValueKind.Array => t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == type),
            _ => false,
        };

    static string? Text(JsonElement node, string name) =>
        !node.TryGetProperty(name, out var v) ? null : v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };

    // JSON-LD prices appear both as numbers (12.99) and strings ("12.99"). Anything else is reported, not guessed.
    static decimal? ReadPrice(JsonElement node, string name, JsonLdResult result)
    {
        if (!node.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (decimal.TryParse(Text(node, name), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)) return d;

        result.Errors.Add($"unreadable price '{(v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())}'");
        return null;
    }
}

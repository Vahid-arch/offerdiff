using System.Text;

namespace ListingProof.Core;

public static class FeedReader
{
    static readonly string[] Columns = ["id", "title", "link", "price", "sale_price", "availability"];
    static readonly string[] Availabilities = ["in_stock", "out_of_stock", "preorder", "backorder"];

    public static FeedReadResult Read(string path) => Parse(File.ReadAllText(path));

    // Row numbers count lines in the file, header = row 1, so they match what you see in an editor.
    public static FeedReadResult Parse(string csv)
    {
        var items = new List<FeedItem>();
        var errors = new List<FeedError>();
        var lines = csv.ReplaceLineEndings("\n").Split('\n');

        var header = SplitLine(lines[0]).Select(h => h.ToLowerInvariant()).ToList();
        var missing = Columns.Where(c => !header.Contains(c)).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new FeedError(1, $"missing column(s): {string.Join(", ", missing)}"));
            return new FeedReadResult(items, errors);
        }

        var firstRowById = new Dictionary<string, int>();
        for (var i = 1; i < lines.Length; i++)
        {
            var row = i + 1;
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var fields = SplitLine(lines[i]);
            if (fields.Count != header.Count)
            {
                errors.Add(new FeedError(row, $"expected {header.Count} fields, found {fields.Count}"));
                continue;
            }

            string Get(string column) => fields[header.IndexOf(column)];

            var id = Get("id");
            if (id == "")
            {
                errors.Add(new FeedError(row, "id is empty"));
                continue;
            }
            if (firstRowById.TryGetValue(id, out var firstRow))
            {
                errors.Add(new FeedError(row, $"duplicate id '{id}', first used on row {firstRow}"));
                continue;
            }
            if (!Price.TryParse(Get("price"), out var price))
            {
                errors.Add(new FeedError(row, $"bad price '{Get("price")}', expected like '12.99 USD'"));
                continue;
            }
            Price? salePrice = null;
            if (Get("sale_price") != "")
            {
                if (!Price.TryParse(Get("sale_price"), out var sale))
                {
                    errors.Add(new FeedError(row, $"bad sale_price '{Get("sale_price")}', expected like '12.99 USD'"));
                    continue;
                }
                salePrice = sale;
            }
            var availability = Get("availability");
            if (!Availabilities.Contains(availability))
            {
                errors.Add(new FeedError(row, $"bad availability '{availability}', expected one of: {string.Join(", ", Availabilities)}"));
                continue;
            }

            // only valid rows claim an id, so a bad first row does not block a corrected one below it
            firstRowById[id] = row;
            items.Add(new FeedItem(row, id, Get("title"), Get("link"), price, salePrice, availability));
        }

        return new FeedReadResult(items, errors);
    }

    // shortcut: quoted fields cannot contain line breaks, switch to a CSV library if real feeds need them
    static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString().Trim());
        return fields;
    }
}

using System.Globalization;

namespace OfferDiff.Core;

public readonly record struct Price(decimal Amount, string Currency)
{
    // Parses Google Merchant style "12.99 USD". Invariant culture so "12,99" is never read as 12.99.
    public static bool TryParse(string? text, out Price price)
    {
        price = default;
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;

        if (!decimal.TryParse(parts[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            return false;

        var currency = parts[1];
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) return false;

        price = new Price(amount, currency);
        return true;
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";
}

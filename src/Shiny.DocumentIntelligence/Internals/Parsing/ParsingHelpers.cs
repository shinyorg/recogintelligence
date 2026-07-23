using System.Globalization;
using System.Text.RegularExpressions;

namespace Shiny.DocumentIntelligence;

/// <summary>Shared money/date scraping used by the heuristic receipt and invoice parsers.</summary>
static partial class ParsingHelpers
{
    // A money amount: optional sign + optional symbol + grouped digits + 2 decimals (the decimals make it
    // a price rather than a quantity/phone number). Captures the symbol so we can infer currency.
    [GeneratedRegex(@"(?<sym>[$£€])?\s?-?(?<num>\d{1,3}(?:,\d{3})+\.\d{2}|\d+\.\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyRegex();

    // ISO 2024-01-31, then D/M/Y or M/D/Y numeric, then "31 Jan 2024" / "Jan 31, 2024".
    [GeneratedRegex(@"\b(\d{4})-(\d{2})-(\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"\b(\d{1,2})\s+([A-Za-z]{3,9})\.?\s+(\d{4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex DayMonthYearRegex();

    [GeneratedRegex(@"\b([A-Za-z]{3,9})\.?\s+(\d{1,2}),?\s+(\d{4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex MonthDayYearRegex();

    /// <summary>The last money amount on a line (totals sit at the right/end), or null.</summary>
    public static decimal? LastMoney(string line) => LastMoneyMatch(line)?.Value;

    /// <summary>
    /// The last money amount on a line, with <b>where</b> it started. The index matters: to strip the amount
    /// off a row you must cut at the text that matched, not search for a reformat of the parsed value —
    /// "1,234.56" never equals its decimal's "0.00" rendering, which used to leave the amount sitting in the
    /// line item's description.
    /// </summary>
    static (decimal Value, int Index)? LastMoneyMatch(string line)
    {
        Match? last = null;
        foreach (Match m in MoneyRegex().Matches(line))
            last = m;
        if (last is null)
            return null;
        return ParseMoney(last.Groups["num"].Value) is { } value ? (value, last.Index) : null;
    }

    /// <summary>The largest money amount anywhere in the text (a fallback for the receipt total).</summary>
    public static decimal? MaxMoney(string text)
    {
        decimal? max = null;
        foreach (Match m in MoneyRegex().Matches(text))
        {
            var v = ParseMoney(m.Groups["num"].Value);
            if (v is not null && (max is null || v > max))
                max = v;
        }
        return max;
    }

    // Standalone ISO currency codes that often trail an amount ("12.99 USD").
    [GeneratedRegex(@"\b(USD|EUR|GBP|CAD|AUD|NZD|JPY|CHF)\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoCurrencyRegex();

    /// <summary>Detects the currency present in the text, mapped to an ISO code — symbol first, then a trailing ISO code.</summary>
    public static string? DetectCurrency(string text)
    {
        foreach (Match m in MoneyRegex().Matches(text))
        {
            var sym = m.Groups["sym"].Value;
            if (sym.Length > 0)
                return sym switch { "$" => "USD", "£" => "GBP", "€" => "EUR", _ => null };
        }
        // No symbol — fall back to an explicit ISO code like "USD"/"EUR".
        var iso = IsoCurrencyRegex().Match(text);
        return iso.Success ? iso.Groups[1].Value : null;
    }

    /// <summary>
    /// Finds the amount on a line whose lowercased text contains one of <paramref name="keywords"/> and none of
    /// <paramref name="exclusions"/>. With <paramref name="takeLast"/>, returns the bottom-most match (totals sit
    /// at the foot of a receipt); otherwise the first.
    /// </summary>
    public static decimal? FindKeyedAmount(IReadOnlyList<string> lines, string[] keywords, string[]? exclusions = null, bool takeLast = false)
    {
        decimal? found = null;
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (exclusions is not null && exclusions.Any(x => lower.Contains(x)))
                continue;
            if (keywords.Any(k => lower.Contains(k)) && LastMoney(line) is { } amount)
            {
                if (!takeLast)
                    return amount;
                found = amount;
            }
        }
        return found;
    }

    /// <summary>Extracts "description … amount" rows, skipping lines whose lowercased text contains a skip keyword.</summary>
    public static IReadOnlyList<LineItem> ExtractLineItems(IReadOnlyList<string> lines, string[] skip)
    {
        var items = new List<LineItem>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (ContainsAny(line, skip))
                continue;
            if (LastMoneyMatch(line) is not { } money)
                continue;

            // Description is the text before the trailing amount.
            var desc = Tidy(line[..money.Index]);

            // Retail receipts routinely put the SKU, quantity and price on one row and the product name on
            // the next ("0979327 69.95 69.95" / "Millie Linen Pull-On"), so an amount row with no words of
            // its own isn't junk — its description is simply the row below.
            if (!LooksLikeWords(desc))
                desc = BorrowDescription(lines, i, skip) ?? desc;

            if (LooksLikeWords(desc))
                items.Add(new LineItem(desc, money.Value));
        }
        return items;
    }

    /// <summary>
    /// The description for an amount row that carries none itself: the nearest following row that reads like
    /// words and has no amount of its own. Stops at the next amount row — that's the next item, not this
    /// one's description — and looks no further than a couple of rows, since a product name follows its price
    /// immediately.
    /// </summary>
    static string? BorrowDescription(IReadOnlyList<string> lines, int from, string[] skip)
    {
        for (var i = from + 1; i < lines.Count && i <= from + 2; i++)
        {
            var candidate = lines[i];
            if (LastMoneyMatch(candidate) is not null)
                return null;
            if (ContainsAny(candidate, skip))
                continue;
            if (LooksLikeWords(candidate))
                return Tidy(candidate);
        }
        return null;
    }

    /// <summary>Strips the currency symbols and column rules ('|', a vertical rule the OCR read as text).</summary>
    static string Tidy(string text) => text.Trim().TrimEnd(' ', '$', '£', '€', '.', '-', '|').Trim();

    /// <summary>
    /// Whether text contains an actual word — three consecutive letters. A bare SKU ("0979327") and a size
    /// code ("0/S/010") both contain letters or none but read as noise; requiring a run of them is what
    /// separates a product name from the rest of the row.
    /// </summary>
    static bool LooksLikeWords(string text)
    {
        var run = 0;
        foreach (var c in text)
        {
            run = Char.IsLetter(c) ? run + 1 : 0;
            if (run >= 3)
                return true;
        }
        return false;
    }

    static bool ContainsAny(string line, string[] keywords)
    {
        var lower = line.ToLowerInvariant();
        return keywords.Any(k => lower.Contains(k));
    }

    static decimal? ParseMoney(string num) =>
        decimal.TryParse(num.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>The first parseable date in the text. Numeric M/D vs D/M is ambiguous; we assume M/D (US) but swap when impossible.</summary>
    public static DateOnly? FirstDate(string text)
    {
        if (IsoDateRegex().Match(text) is { Success: true } iso)
            return Build(int.Parse(iso.Groups[1].Value), int.Parse(iso.Groups[2].Value), int.Parse(iso.Groups[3].Value));

        if (DayMonthYearRegex().Match(text) is { Success: true } dmy && Month(dmy.Groups[2].Value) is { } mo1)
            return Build(int.Parse(dmy.Groups[3].Value), mo1, int.Parse(dmy.Groups[1].Value));

        if (MonthDayYearRegex().Match(text) is { Success: true } mdy && Month(mdy.Groups[1].Value) is { } mo2)
            return Build(int.Parse(mdy.Groups[3].Value), mo2, int.Parse(mdy.Groups[2].Value));

        if (NumericDateRegex().Match(text) is { Success: true } num)
        {
            int a = int.Parse(num.Groups[1].Value), b = int.Parse(num.Groups[2].Value);
            var year = NormalizeYear(int.Parse(num.Groups[3].Value));
            // Assume month-first; if that month is impossible (>12) but the other field is a valid month, swap.
            return a > 12 && b <= 12 ? Build(year, b, a) : Build(year, a, b);
        }
        return null;
    }

    static int NormalizeYear(int y) => y >= 100 ? y : (y < 70 ? 2000 + y : 1900 + y);

    static int? Month(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        int[] _ = [];
        string[] months = ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];
        for (var i = 0; i < months.Length; i++)
            if (months[i].StartsWith(n[..Math.Min(3, n.Length)], StringComparison.Ordinal))
                return i + 1;
        return null;
    }

    static DateOnly? Build(int y, int m, int d)
    {
        if (m is < 1 or > 12 || d is < 1 or > 31)
            return null;
        try { return new DateOnly(y, m, d); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}

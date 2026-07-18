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
    public static decimal? LastMoney(string line)
    {
        Match? last = null;
        foreach (Match m in MoneyRegex().Matches(line))
            last = m;
        return last is null ? null : ParseMoney(last.Groups["num"].Value);
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
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (skip.Any(s => lower.Contains(s)))
                continue;
            if (LastMoney(line) is not { } amount)
                continue;

            // Description is the text before the trailing amount.
            var idx = line.LastIndexOf(amount.ToString("0.00"), StringComparison.Ordinal);
            var desc = (idx > 0 ? line[..idx] : line).TrimEnd(' ', '$', '£', '€', '.', '-').Trim();
            if (desc.Length > 0 && desc.Any(Char.IsLetter))
                items.Add(new LineItem(desc, amount));
        }
        return items;
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

using System.Text.RegularExpressions;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Parses the front of a payment card from OCR text: PAN, expiry, cardholder name and network.
/// </summary>
/// <remarks>
/// <para>
/// The PAN is what makes this tractable. Card numbers carry a Luhn check digit, so instead of guessing
/// which digit run on a noisy card is the number, every candidate is tested and only a Luhn-valid one is
/// accepted. That single constraint rejects almost all OCR noise — dates, phone numbers, the odd
/// misread — without any layout heuristics.
/// </para>
/// <para>
/// <b>The CVV is deliberately not parsed.</b> See <see cref="CreditCardData"/>.
/// </para>
/// </remarks>
public static partial class CreditCardParser
{
    // A run of 13-19 digits that may be split into groups by spaces or dashes, as cards are printed.
    [GeneratedRegex(@"(?<!\d)(?:\d[ \-]?){12,18}\d(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PanCandidateRegex();

    // MM/YY or MM/YYYY. Cards also print a "member since" date in the same shape, hence the labelling pass.
    [GeneratedRegex(@"(?<!\d)(0[1-9]|1[0-2])\s*[/\-]\s*(\d{4}|\d{2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ExpiryRegex();

    // An embossed name: 2+ uppercase words. Words may be a single letter, because middle initials
    // ("JANE Q CARDHOLDER") and initialled first names ("J SMITH") are both common on cards.
    [GeneratedRegex(@"^[A-Z][A-Z'\-]*(?: [A-Z][A-Z'\-.]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    // Labels that precede the expiry, and text that looks like a name but isn't.
    static readonly string[] ExpiryLabels = ["valid thru", "valid through", "expires", "exp", "good thru", "valid to"];
    static readonly string[] SinceLabels = ["member since", "since", "issued"];
    static readonly string[] NonNameWords =
    [
        "VALID", "THRU", "THROUGH", "EXPIRES", "MEMBER", "SINCE", "GOOD", "DEBIT", "CREDIT", "BANK",
        "CARD", "PLATINUM", "GOLD", "SILVER", "CLASSIC", "BUSINESS", "REWARDS", "SIGNATURE", "INFINITE",
        "WORLD", "ELITE", "CUSTOMER", "SERVICE", "AUTHORIZED", "SIGNATURE", "ELECTRONIC", "USE", "ONLY"
    ];

    /// <summary>
    /// Parse a card from OCR text, or return null when no Luhn-valid PAN is present. A non-null result
    /// always has a plausible number; expiry and name are best-effort and may be null.
    /// </summary>
    public static CreditCardData? TryParse(string ocrText)
    {
        if (String.IsNullOrWhiteSpace(ocrText))
            return null;

        var lines = ocrText
            .Split('\n', '\r')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var number = FindPan(ocrText, lines);
        if (number is null)
            return null;

        var (month, year) = FindExpiry(lines);
        return new CreditCardData(
            number,
            DetectNetwork(number),
            month,
            year,
            FindCardholder(lines, number),
            IsLuhnValid(number));
    }

    /// <summary>True when <paramref name="digits"/> satisfies the Luhn check digit. Public because it's useful on its own.</summary>
    public static bool IsLuhnValid(string digits)
    {
        if (digits.Length < 2)
            return false;

        var sum = 0;
        var doubling = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var c = digits[i];
            if (!Char.IsAsciiDigit(c))
                return false;

            var d = c - '0';
            if (doubling)
            {
                d *= 2;
                if (d > 9)
                    d -= 9;
            }
            sum += d;
            doubling = !doubling;
        }
        return sum % 10 == 0;
    }

    /// <summary>The network implied by the issuer identification number (the leading digits).</summary>
    public static CardNetwork DetectNetwork(string number)
    {
        if (number.Length < 2)
            return CardNetwork.Unknown;

        int Prefix(int len) => len <= number.Length ? int.Parse(number[..len]) : -1;
        var p1 = Prefix(1);
        var p2 = Prefix(2);
        var p3 = Prefix(3);
        var p4 = Prefix(4);

        if (p1 == 4)
            return CardNetwork.Visa;
        if (p2 is 34 or 37)
            return CardNetwork.AmericanExpress;
        if (p2 is >= 51 and <= 55 || p4 is >= 2221 and <= 2720)
            return CardNetwork.Mastercard;
        if (p4 == 6011 || p2 == 65 || p3 is >= 644 and <= 649)
            return CardNetwork.Discover;
        if (p4 is >= 3528 and <= 3589)
            return CardNetwork.JCB;
        if (p3 is >= 300 and <= 305 || p2 is 36 or 38 or 39)
            return CardNetwork.DinersClub;
        if (p2 == 62)
            return CardNetwork.UnionPay;
        if (p2 is 50 or 56 or 57 or 58 || p4 is 6304 or 6759)
            return CardNetwork.Maestro;

        return CardNetwork.Unknown;
    }

    /// <summary>
    /// The first Luhn-valid digit run of a plausible PAN length. Whole-text first (a number wrapped across
    /// lines still reads correctly), then per line as a fallback.
    /// </summary>
    static string? FindPan(string text, IReadOnlyList<string> lines)
    {
        if (ScanForPan(text) is { } fromText)
            return fromText;

        foreach (var line in lines)
            if (ScanForPan(line) is { } fromLine)
                return fromLine;

        return null;
    }

    static string? ScanForPan(string text)
    {
        string? firstUnvalidated = null;
        foreach (Match m in PanCandidateRegex().Matches(text))
        {
            var digits = new string(m.Value.Where(Char.IsAsciiDigit).ToArray());

            // Try the whole run, then progressively shorter prefixes: OCR often glues a trailing digit
            // (an expiry, a service code) onto the number.
            for (var len = Math.Min(19, digits.Length); len >= 13; len--)
            {
                var candidate = digits[..len];
                if (!IsPlausibleLength(candidate))
                    continue;
                if (IsLuhnValid(candidate))
                    return candidate;
                firstUnvalidated ??= candidate;
            }
        }

        // Nothing passed Luhn. Returning the best-looking candidate anyway lets the caller show the user
        // what was read and decide — IsValid on the result reports that it failed.
        return firstUnvalidated;
    }

    /// <summary>Amex is 15 digits, Diners 14, everything mainstream 16 (up to 19 for some UnionPay/Maestro).</summary>
    static bool IsPlausibleLength(string digits) => digits.Length switch
    {
        15 => DetectNetwork(digits) == CardNetwork.AmericanExpress,
        14 => DetectNetwork(digits) == CardNetwork.DinersClub,
        13 => DetectNetwork(digits) == CardNetwork.Visa,
        16 or 17 or 18 or 19 => true,
        _ => false
    };

    /// <summary>
    /// Expiry, preferring a date on a line labelled "VALID THRU"/"EXPIRES". Cards often also print a
    /// "MEMBER SINCE" date in the same MM/YY shape, so unlabelled text falls back to the latest date found —
    /// the expiry is always later than the issue date.
    /// </summary>
    static (int? Month, int? Year) FindExpiry(IReadOnlyList<string> lines)
    {
        (int Month, int Year)? labelled = null;
        (int Month, int Year)? latest = null;

        for (var i = 0; i < lines.Count; i++)
        {
            var lower = lines[i].ToLowerInvariant();
            var isSince = SinceLabels.Any(lower.Contains) && !ExpiryLabels.Any(lower.Contains);

            foreach (Match m in ExpiryRegex().Matches(lines[i]))
            {
                var month = int.Parse(m.Groups[1].Value);
                var raw = m.Groups[2].Value;
                var year = raw.Length == 4 ? int.Parse(raw) : 2000 + int.Parse(raw);
                var value = (month, year);

                if (isSince)
                    continue;

                // Labelled on this line, or on the previous one ("VALID THRU" often sits above the date).
                var labelHere = ExpiryLabels.Any(lower.Contains) ||
                                (i > 0 && ExpiryLabels.Any(lines[i - 1].ToLowerInvariant().Contains));
                if (labelHere)
                    labelled ??= value;

                if (latest is null || year > latest.Value.Year || (year == latest.Value.Year && month > latest.Value.Month))
                    latest = value;
            }
        }

        var chosen = labelled ?? latest;
        return (chosen?.Month, chosen?.Year);
    }

    /// <summary>
    /// The embossed name: an all-caps line of 2+ words that isn't card furniture ("VALID THRU", the bank
    /// name, "DEBIT"). Cards print it last, so the bottom-most match wins.
    /// </summary>
    static string? FindCardholder(IReadOnlyList<string> lines, string number)
    {
        string? best = null;
        foreach (var line in lines)
        {
            var candidate = line.Trim();
            if (candidate.Length is < 5 or > 26)
                continue;
            if (candidate.Any(Char.IsDigit))
                continue;
            if (!NameRegex().IsMatch(candidate))
                continue;

            var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => NonNameWords.Contains(w.TrimEnd('.'))))
                continue;

            best = candidate;
        }
        return best;
    }
}

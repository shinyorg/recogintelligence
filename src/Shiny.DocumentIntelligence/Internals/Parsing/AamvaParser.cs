using System.Globalization;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Parses the AAMVA-standard payload decoded from the PDF417 barcode on the back of US/Canada driver's
/// licenses and state IDs. The payload is a header followed by 3-letter element IDs (e.g. <c>DCS</c> =
/// family name, <c>DAQ</c> = license number) each terminated by a line feed.
/// </summary>
/// <remarks>
/// This is the reliable license path: it's structured, standardized data — no OCR guessing. We surface the
/// common elements as named properties and keep the full set in <see cref="LicenseData.Elements"/>. Date
/// element layout differs by country (US <c>MMDDCCYY</c>, Canada <c>CCYYMMDD</c>); we use the <c>DCG</c>
/// country element when present and fall back to a year-position heuristic otherwise.
/// </remarks>
public static class AamvaParser
{
    /// <summary>Returns true if the payload looks like an AAMVA blob (so callers can pick this barcode out of several).</summary>
    public static bool LooksLikeAamva(string payload) =>
        payload.Contains("ANSI ", StringComparison.Ordinal) || payload.Contains("AAMVA", StringComparison.Ordinal);

    /// <summary>Parse a decoded PDF417 AAMVA payload. Returns null when it doesn't look like AAMVA at all.</summary>
    public static LicenseData? TryParse(string payload)
    {
        if (String.IsNullOrWhiteSpace(payload) || !LooksLikeAamva(payload))
            return null;

        var elements = ReadElements(payload);
        if (elements.Count == 0)
            return null;

        var country = Get(elements, "DCG");
        string? G(string code) => Get(elements, code);

        // First name: DAC (newer) or DCT (given-names, older / combined).
        var firstName = G("DAC") ?? G("DCT");

        return new LicenseData(
            FirstName: firstName,
            MiddleName: G("DAD"),
            LastName: G("DCS") ?? G("DAB"),
            DateOfBirth: ParseDate(G("DBB"), country),
            LicenseNumber: G("DAQ"),
            IssueDate: ParseDate(G("DBD"), country),
            ExpiryDate: ParseDate(G("DBA"), country),
            Sex: MapSex(G("DBC")),
            Address: G("DAG"),
            City: G("DAI"),
            State: G("DAJ"),
            PostalCode: NormalizePostal(G("DAK")),
            Elements: elements
        );
    }

    static Dictionary<string, string> ReadElements(string payload)
    {
        // Elements are separated by LF/CR. Each token is a 3-letter code + value, optionally prefixed by a
        // 2-char subfile type (DL/ID/EN/Z*) at the start of a subfile — strip that so the code lines up.
        var elements = new Dictionary<string, string>(StringComparer.Ordinal);
        var tokens = payload.Split('\n', '\r');

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            // Drop a leading subfile designator like "DL" when it's followed by a real 3-letter element code.
            if (token.Length >= 5 && IsSubfileType(token.AsSpan(0, 2)) && IsCode(token.AsSpan(2, 3)))
                token = token[2..];

            if (token.Length < 3 || !IsCode(token.AsSpan(0, 3)))
                continue;

            var code = token[..3];
            var value = token[3..].Trim();
            if (value.Length > 0 && !elements.ContainsKey(code))
                elements[code] = value;
        }
        return elements;
    }

    static bool IsSubfileType(ReadOnlySpan<char> s) =>
        s is "DL" or "ID" or "EN" || (s.Length == 2 && s[0] == 'Z' && Char.IsAsciiLetterUpper(s[1]));

    static bool IsCode(ReadOnlySpan<char> s)
    {
        if (s.Length != 3)
            return false;
        foreach (var c in s)
            if (!Char.IsAsciiLetterUpper(c))
                return false;
        return true;
    }

    static string? Get(IReadOnlyDictionary<string, string> e, string code) =>
        e.TryGetValue(code, out var v) && v.Length > 0 ? v : null;

    static string? MapSex(string? code) => code switch
    {
        "1" => "M",
        "2" => "F",
        _ => null
    };

    static string? NormalizePostal(string? postal)
    {
        if (String.IsNullOrWhiteSpace(postal))
            return null;

        var p = postal.Trim();
        // AAMVA encodes a US ZIP+4 as 9 digits, zero-filling the +4 when it's absent. Don't blanket-trim
        // zeros (ZIPs legitimately end in 0) — split 5 + 4 and drop the +4 only when it's all-zero filler.
        if (p.Length == 9 && p.All(Char.IsDigit))
        {
            var zip5 = p[..5];
            var plus4 = p[5..];
            return plus4 == "0000" ? zip5 : $"{zip5}-{plus4}";
        }
        return p;
    }

    static DateOnly? ParseDate(string? value, string? country)
    {
        if (value is null || value.Length != 8 || !value.All(Char.IsDigit))
            return null;

        // Canada uses CCYYMMDD; the US uses MMDDCCYY. Prefer the country hint, fall back to year position.
        var canadaLayout =
            String.Equals(country, "CAN", StringComparison.OrdinalIgnoreCase) ||
            IsYear(value[..4]);

        int year, month, day;
        if (canadaLayout)
        {
            year = int.Parse(value[..4], CultureInfo.InvariantCulture);
            month = int.Parse(value[4..6], CultureInfo.InvariantCulture);
            day = int.Parse(value[6..8], CultureInfo.InvariantCulture);
        }
        else
        {
            month = int.Parse(value[..2], CultureInfo.InvariantCulture);
            day = int.Parse(value[2..4], CultureInfo.InvariantCulture);
            year = int.Parse(value[4..8], CultureInfo.InvariantCulture);
        }

        if (month is < 1 or > 12 || day is < 1 or > 31)
            return null;
        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    static bool IsYear(string four) =>
        int.TryParse(four, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1900 and <= 2100;
}

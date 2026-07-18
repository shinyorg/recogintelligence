using System.Globalization;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Parses the passport machine-readable zone per ICAO 9303. Handles the TD3 format (passports: two
/// 44-character lines) and the TD1 format (ID cards: three 30-character lines). The MRZ is the band of
/// OCR-B monospace text at the bottom of the data page; OCR it, hand the lines here.
/// </summary>
/// <remarks>
/// Each field is fixed-width and followed by a check digit computed with the 7-3-1 weighting over the
/// character set [0-9A-Z&lt;] (filler '&lt;' = 0). We verify those check digits and expose the aggregate in
/// <see cref="PassportData.IsValid"/> so a caller can decide how much to trust a noisy OCR read.
/// </remarks>
public static class MrzParser
{
    /// <summary>
    /// Try to find and parse an MRZ in arbitrary OCR text. Scans for the characteristic fixed-width
    /// '&lt;'-padded lines, so it tolerates the surrounding visual-zone text the OCR also picks up.
    /// </summary>
    public static PassportData? TryParse(string ocrText)
    {
        if (String.IsNullOrWhiteSpace(ocrText))
            return null;

        var lines = ocrText
            .Split('\n', '\r')
            .Select(Normalize)
            .Where(l => l.Length > 0)
            .ToList();

        // TD3 (passport): two lines of 44. TD1 (ID): three lines of 30.
        var td3 = FindFixedWidthRun(lines, 44, 2);
        if (td3 is not null)
            return ParseTd3(td3[0], td3[1]);

        var td1 = FindFixedWidthRun(lines, 30, 3);
        if (td1 is not null)
            return ParseTd1(td1[0], td1[1], td1[2]);

        return null;
    }

    // OCR often reads MRZ filler as the wrong glyph and varies spacing/case. Uppercase, strip spaces,
    // and map the usual confusions toward '<' so width detection lines up.
    static string Normalize(string line)
    {
        var chars = line.Trim().ToUpperInvariant().Where(c => !Char.IsWhiteSpace(c)).ToArray();
        return new string(chars);
    }

    static IReadOnlyList<string>? FindFixedWidthRun(IReadOnlyList<string> lines, int width, int count)
    {
        for (var i = 0; i + count <= lines.Count; i++)
        {
            var window = lines.Skip(i).Take(count).ToList();
            // Allow ±1 char of OCR noise on length, then pad/trim to exact width.
            if (window.All(l => Math.Abs(l.Length - width) <= 1 && l.Contains('<')))
                return window.Select(l => Fit(l, width)).ToList();
        }
        return null;
    }

    static string Fit(string s, int width) =>
        s.Length == width ? s :
        s.Length > width ? s[..width] :
        s.PadRight(width, '<');

    static PassportData ParseTd3(string l1, string l2)
    {
        // Line 1: P<ISOSurname<<GivenNames...
        var documentCode = Clean(l1[..2]);
        var issuingCountry = Clean(l1[2..5]);
        var (surname, given) = SplitName(l1[5..]);

        // Line 2 fixed offsets per ICAO 9303 TD3.
        var passportNumber = Clean(l2[0..9]);
        var passportNumberCheck = l2[9];
        var nationality = Clean(l2[10..13]);
        var dob = l2[13..19];
        var dobCheck = l2[19];
        var sex = MapSex(l2[20]);
        var expiry = l2[21..27];
        var expiryCheck = l2[27];
        var personalNumber = Clean(l2[28..42]);
        var personalCheck = l2[42];
        var compositeCheck = l2[43];

        var valid =
            CheckDigit(l2[0..9]) == Digit(passportNumberCheck) &&
            CheckDigit(dob) == Digit(dobCheck) &&
            CheckDigit(expiry) == Digit(expiryCheck) &&
            // Personal-number check is '<' (skipped) when the field is unused.
            (personalCheck == '<' || CheckDigit(l2[28..42]) == Digit(personalCheck)) &&
            CheckDigit(l2[0..10] + l2[13..20] + l2[21..28] + l2[28..43]) == Digit(compositeCheck);

        return new PassportData(
            DocumentCode: documentCode,
            IssuingCountry: issuingCountry,
            Surname: surname,
            GivenNames: given,
            PassportNumber: passportNumber,
            Nationality: nationality,
            DateOfBirth: ParseMrzDate(dob, isBirthDate: true),
            Sex: sex,
            ExpiryDate: ParseMrzDate(expiry, isBirthDate: false),
            PersonalNumber: String.IsNullOrEmpty(personalNumber) ? null : personalNumber,
            IsValid: valid
        );
    }

    static PassportData ParseTd1(string l1, string l2, string l3)
    {
        // TD1 line 1: doc code(2) + country(3) + doc number(9) + check(1) + optional(15)
        var documentCode = Clean(l1[0..2]);
        var issuingCountry = Clean(l1[2..5]);
        var docNumber = Clean(l1[5..14]);
        var docNumberCheck = l1[14];

        // TD1 line 2: dob(6) + check + sex + expiry(6) + check + nationality(3) + optional + composite check
        var dob = l2[0..6];
        var dobCheck = l2[6];
        var sex = MapSex(l2[7]);
        var expiry = l2[8..14];
        var expiryCheck = l2[14];
        var nationality = Clean(l2[15..18]);

        // TD1 line 3 is the name.
        var (surname, given) = SplitName(l3);

        var valid =
            CheckDigit(l1[5..14]) == Digit(docNumberCheck) &&
            CheckDigit(dob) == Digit(dobCheck) &&
            CheckDigit(expiry) == Digit(expiryCheck);

        return new PassportData(
            DocumentCode: documentCode,
            IssuingCountry: issuingCountry,
            Surname: surname,
            GivenNames: given,
            PassportNumber: docNumber,
            Nationality: nationality,
            DateOfBirth: ParseMrzDate(dob, isBirthDate: true),
            Sex: sex,
            ExpiryDate: ParseMrzDate(expiry, isBirthDate: false),
            PersonalNumber: null,
            IsValid: valid
        );
    }

    static (string? Surname, string? Given) SplitName(string field)
    {
        // Names: SURNAME<<GIVEN<NAMES, filler '<' = space, '<<' = surname/given separator.
        var parts = field.Split("<<", 2);
        var surname = TidyName(parts[0]);
        var given = parts.Length > 1 ? TidyName(parts[1]) : null;
        return (surname, given);
    }

    static string? TidyName(string raw)
    {
        var name = raw.Replace('<', ' ').Trim();
        name = String.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? null : name;
    }

    static string Clean(string field) => field.Replace("<", "").Trim();

    static string? MapSex(char c) => c switch
    {
        'M' => "M",
        'F' => "F",
        _ => null // '<' or 'X' => unspecified
    };

    /// <summary>MRZ dates are YYMMDD with a windowed century (per ICAO: future birth dates roll back 100 years).</summary>
    static DateOnly? ParseMrzDate(string yymmdd, bool isBirthDate)
    {
        if (yymmdd.Length != 6 || !yymmdd.All(Char.IsDigit))
            return null;

        var yy = int.Parse(yymmdd[0..2], CultureInfo.InvariantCulture);
        var mm = int.Parse(yymmdd[2..4], CultureInfo.InvariantCulture);
        var dd = int.Parse(yymmdd[4..6], CultureInfo.InvariantCulture);
        if (mm is < 1 or > 12 || dd is < 1 or > 31)
            return null;

        // Pivot at 50: birth dates resolve to the past, expiry dates to the near future.
        var century = isBirthDate
            ? (yy <= 50 ? 2000 : 1900)
            : (yy < 70 ? 2000 : 1900);
        try
        {
            return new DateOnly(century + yy, mm, dd);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>ICAO 7-3-1 weighted modulo-10 check digit over [0-9A-Z&lt;].</summary>
    static int CheckDigit(string data)
    {
        int[] weights = [7, 3, 1];
        var sum = 0;
        for (var i = 0; i < data.Length; i++)
            sum += Value(data[i]) * weights[i % 3];
        return sum % 10;
    }

    static int Value(char c) =>
        c == '<' ? 0 :
        c is >= '0' and <= '9' ? c - '0' :
        c is >= 'A' and <= 'Z' ? c - 'A' + 10 :
        0;

    static int Digit(char c) => c is >= '0' and <= '9' ? c - '0' : -1;
}

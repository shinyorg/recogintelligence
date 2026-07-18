using System.Text.RegularExpressions;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Best-effort field extraction from invoice OCR text. Like <see cref="ReceiptParser"/> this is heuristic —
/// invoice layouts are free-form — but it additionally pulls an invoice number and a due date, which
/// receipts don't carry. Every field is a hint that may need human correction.
/// </summary>
public static partial class InvoiceParser
{
    [GeneratedRegex(@"invoice\s*(?:no\.?|number|#|num)?\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9\-]{2,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvoiceNumberRegex();

    static readonly string[] TotalExclusions = ["subtotal", "total tax", "total discount", "total items"];
    static readonly string[] ItemSkip = ["total", "subtotal", "tax", "vat", "gst", "hst", "balance", "amount due", "due"];

    public static InvoiceData Parse(string ocrText)
    {
        var text = ocrText ?? string.Empty;
        var lines = text
            .Split('\n', '\r')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var vendor = lines.FirstOrDefault(l => l.Any(Char.IsLetter));

        string? invoiceNumber = null;
        if (InvoiceNumberRegex().Match(text) is { Success: true } m)
            invoiceNumber = m.Groups[1].Value;

        var total = ParsingHelpers.FindKeyedAmount(
            lines,
            ["balance due", "amount due", "total due", "grand total", "total"],
            TotalExclusions,
            takeLast: true);
        var subtotal = ParsingHelpers.FindKeyedAmount(lines, ["subtotal", "sub total"]);
        var tax = ParsingHelpers.FindKeyedAmount(lines, ["sales tax", "tax", "vat", "gst", "hst"], ["subtotal"]);
        total ??= ParsingHelpers.MaxMoney(text);

        // Date keywords: a line mentioning "due" gives the due date; the first other date is the invoice date.
        var dueDate = FindKeyedDate(lines, ["due date", "payment due", "due"]);
        var invoiceDate = FindKeyedDate(lines, ["invoice date", "date of issue", "issued", "date"])
            ?? ParsingHelpers.FirstDate(text);

        return new InvoiceData(
            Vendor: vendor,
            InvoiceNumber: invoiceNumber,
            InvoiceDate: invoiceDate,
            DueDate: dueDate,
            Subtotal: subtotal,
            Tax: tax,
            Total: total,
            Currency: ParsingHelpers.DetectCurrency(text),
            Items: ParsingHelpers.ExtractLineItems(lines, ItemSkip)
        );
    }

    static DateOnly? FindKeyedDate(IReadOnlyList<string> lines, string[] keywords)
    {
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (keywords.Any(k => lower.Contains(k)) && ParsingHelpers.FirstDate(line) is { } date)
                return date;
        }
        return null;
    }
}

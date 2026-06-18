namespace Shiny.DocumentIntelligence;

/// <summary>
/// Best-effort field extraction from receipt OCR text. There is no receipt standard, so this is heuristic:
/// the merchant is the top line, totals/tax/subtotal are keyword-anchored, and line items are the remaining
/// "description ... amount" rows. Treat every field as a hint a human may need to correct.
/// </summary>
public static class ReceiptParser
{
    // Decoy "total …" lines that are not the grand total.
    static readonly string[] TotalExclusions =
        ["subtotal", "total tax", "total savings", "total discount", "total items", "total qty", "total number"];

    // Lines that carry an amount but aren't purchased items (tax lines, tenders, card/loyalty rows).
    static readonly string[] ItemSkip =
    [
        "total", "subtotal", "tax", "vat", "gst", "hst", "change", "cash", "balance", "amount due", "tender",
        "visa", "mastercard", "amex", "discover", "debit", "credit", "card", "approval", "auth", "account"
    ];

    public static ReceiptData Parse(string ocrText)
    {
        var text = ocrText ?? string.Empty;
        var lines = text
            .Split('\n', '\r')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var merchant = lines.FirstOrDefault(l => l.Any(Char.IsLetter));

        // Total: take the bottom-most "total"/"amount due" line, skipping subtotal/tax/savings decoys.
        var total = ParsingHelpers.FindKeyedAmount(
            lines,
            ["grand total", "balance due", "amount due", "total due", "total"],
            TotalExclusions,
            takeLast: true);
        var subtotal = ParsingHelpers.FindKeyedAmount(lines, ["subtotal", "sub total"]);
        var tax = ParsingHelpers.FindKeyedAmount(lines, ["sales tax", "tax", "vat", "gst", "hst"], ["subtotal"]);

        // Fall back to the largest amount on the receipt when no "total" keyword was found.
        total ??= ParsingHelpers.MaxMoney(text);

        return new ReceiptData(
            Merchant: merchant,
            Date: ParsingHelpers.FirstDate(text),
            Subtotal: subtotal,
            Tax: tax,
            Total: total,
            Currency: ParsingHelpers.DetectCurrency(text),
            Items: ParsingHelpers.ExtractLineItems(lines, ItemSkip)
        );
    }
}

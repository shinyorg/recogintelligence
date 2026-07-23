namespace Shiny.DocumentIntelligence;

/// <summary>
/// The kind of document to extract structured data from. Selects which extraction strategy
/// <see cref="IDocumentExtractor"/> uses: OCR + heuristic parsing for <see cref="Receipt"/>/<see cref="Invoice"/>,
/// PDF417 barcode decode for <see cref="DriversLicense"/>, MRZ OCR for <see cref="Passport"/>, and
/// Luhn-validated OCR for <see cref="CreditCard"/>.
/// </summary>
public enum DocumentType
{
    /// <summary>No type-specific parsing — return the raw recognized text only.</summary>
    Unknown,

    /// <summary>A point-of-sale receipt. OCR + heuristic parse of merchant/total/tax/date.</summary>
    Receipt,

    /// <summary>A vendor invoice. OCR + heuristic parse of vendor/number/dates/total.</summary>
    Invoice,

    /// <summary>A driver's license or state ID. Decodes the AAMVA PDF417 barcode (usually on the back).</summary>
    DriversLicense,

    /// <summary>A passport. OCR + ICAO 9303 parse of the machine-readable zone (MRZ).</summary>
    Passport,

    /// <summary>
    /// A payment card, front side. OCR + Luhn-validated PAN, expiry, cardholder and network. The CVV is
    /// never read — see <see cref="CreditCardData"/>.
    /// </summary>
    CreditCard
}

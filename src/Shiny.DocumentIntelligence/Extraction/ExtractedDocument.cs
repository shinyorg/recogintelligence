namespace Shiny.DocumentIntelligence;

/// <summary>
/// The structured result of <see cref="IDocumentExtractor"/> extraction. Exactly one of the typed
/// payloads is populated according to <see cref="Type"/> (or none, when extraction couldn't parse the
/// document — <see cref="RawText"/> still carries whatever OCR produced for fallback/diagnostics).
/// </summary>
public class ExtractedDocument
{
    /// <summary>The requested document type.</summary>
    public required DocumentType Type { get; init; }

    /// <summary>The raw OCR text behind the parse (empty for the pure-barcode license path). Always set for diagnostics.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>Populated when <see cref="Type"/> is <see cref="DocumentType.Receipt"/> and parsing succeeded.</summary>
    public ReceiptData? Receipt { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <see cref="DocumentType.Invoice"/> and parsing succeeded.</summary>
    public InvoiceData? Invoice { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <see cref="DocumentType.DriversLicense"/> and the PDF417 decoded.</summary>
    public LicenseData? License { get; init; }

    /// <summary>Populated when <see cref="Type"/> is <see cref="DocumentType.Passport"/> and the MRZ parsed.</summary>
    public PassportData? Passport { get; init; }

    /// <summary>
    /// Populated when <see cref="Type"/> is <see cref="DocumentType.CreditCard"/> and a card number was
    /// read. Check <see cref="CreditCardData.IsValid"/> — a result is returned even when the Luhn check
    /// fails, so the UI can show what was read rather than silently reporting nothing.
    /// </summary>
    public CreditCardData? CreditCard { get; init; }

    /// <summary>
    /// Dates, addresses, phone numbers and links the platform data detector found in <see cref="RawText"/>.
    /// Empty where no detector is available (see <see cref="IDataDetector"/>) — this is enrichment on top of
    /// the typed payloads, never a replacement for them.
    /// </summary>
    public IReadOnlyList<DetectedEntity> Entities { get; init; } = [];

    /// <summary>True when the type-specific parser produced a payload. False means OCR ran but nothing structured came out.</summary>
    public bool HasStructuredData =>
        this.Receipt is not null || this.Invoice is not null || this.License is not null ||
        this.Passport is not null || this.CreditCard is not null;
}

/// <summary>A single line item on a receipt or invoice.</summary>
/// <param name="Description">The item text.</param>
/// <param name="Amount">The line amount, when one could be parsed.</param>
public record LineItem(string Description, decimal? Amount);

/// <summary>Fields parsed from a point-of-sale receipt. Every field is best-effort and may be null.</summary>
public record ReceiptData(
    string? Merchant,
    DateOnly? Date,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Total,
    string? Currency,
    IReadOnlyList<LineItem> Items
);

/// <summary>Fields parsed from a vendor invoice. Every field is best-effort and may be null.</summary>
public record InvoiceData(
    string? Vendor,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    DateOnly? DueDate,
    decimal? Subtotal,
    decimal? Tax,
    decimal? Total,
    string? Currency,
    IReadOnlyList<LineItem> Items
);

/// <summary>
/// Fields decoded from a driver's license / state-ID AAMVA PDF417 barcode. Keys follow the AAMVA
/// element IDs (e.g. <c>DAC</c> = first name); the common ones are surfaced as named properties and the
/// full set is in <see cref="Elements"/>.
/// </summary>
public record LicenseData(
    string? FirstName,
    string? MiddleName,
    string? LastName,
    DateOnly? DateOfBirth,
    string? LicenseNumber,
    DateOnly? IssueDate,
    DateOnly? ExpiryDate,
    string? Sex,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    IReadOnlyDictionary<string, string> Elements
);

/// <summary>
/// Fields parsed from a passport machine-readable zone (ICAO 9303 TD3, two 44-char lines).
/// <see cref="IsValid"/> reflects whether the MRZ check digits verified.
/// </summary>
public record PassportData(
    string? DocumentCode,
    string? IssuingCountry,
    string? Surname,
    string? GivenNames,
    string? PassportNumber,
    string? Nationality,
    DateOnly? DateOfBirth,
    string? Sex,
    DateOnly? ExpiryDate,
    string? PersonalNumber,
    bool IsValid
);

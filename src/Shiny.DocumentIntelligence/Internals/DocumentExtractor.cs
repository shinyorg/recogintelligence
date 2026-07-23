namespace Shiny.DocumentIntelligence;

/// <summary>
/// Default <see cref="IDocumentExtractor"/>: cross-platform glue with no native code. It routes each
/// <see cref="DocumentType"/> to the right on-device primitive — OCR via <see cref="ITextRecognizer"/> for
/// receipts/invoices/passports, PDF417 decode via <see cref="IBarcodeReader"/> for licenses — and then
/// hands the result to the matching pure-C# parser. The native bits are injected, so this class is the same
/// on every platform and is fully unit-testable with fakes.
/// </summary>
public class DocumentExtractor(
    ITextRecognizer textRecognizer,
    IBarcodeReader barcodeReader,
    IDataDetector? dataDetector = null
) : IDocumentExtractor
{
    public async Task<ExtractedDocument> ExtractAsync(byte[] imageData, DocumentType type, CancellationToken cancellationToken = default)
    {
        if (type == DocumentType.DriversLicense)
            return await ExtractLicenseAsync([imageData], cancellationToken).ConfigureAwait(false);

        var text = await this.RecognizeAsync(imageData, type, cancellationToken).ConfigureAwait(false);
        return this.Build(type, text.FullText);
    }

    public async Task<ExtractedDocument> ExtractAsync(DocumentScanResult scan, DocumentType type, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var pages = scan.Pages.Select(p => p.ImageData).ToList();
        if (pages.Count == 0)
            return new ExtractedDocument { Type = type };

        if (type == DocumentType.DriversLicense)
            return await ExtractLicenseAsync(pages, cancellationToken).ConfigureAwait(false);

        // OCR every page and concatenate, so multi-page invoices / a passport shot with extra pages parse as one.
        var texts = new List<string>(pages.Count);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recognized = await this.RecognizeAsync(page, type, cancellationToken).ConfigureAwait(false);
            if (recognized.FullText.Length > 0)
                texts.Add(recognized.FullText);
        }
        return this.Build(type, string.Join('\n', texts));
    }

    async Task<RecognizedText> RecognizeAsync(byte[] image, DocumentType type, CancellationToken ct)
    {
        if (!textRecognizer.IsSupported)
            throw new PlatformNotSupportedException("On-device text recognition is not available on this platform.");
        return await textRecognizer.RecognizeAsync(image, OptionsFor(type), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// OCR tuning per document type. Cards and passport MRZ lines are alphanumeric strings with no
    /// linguistic context, so the language model can only hurt them: it has no words to reason about and
    /// will happily nudge a digit or an MRZ filler toward something "more likely". Prose documents keep it.
    /// </summary>
    static TextRecognitionOptions OptionsFor(DocumentType type) => type switch
    {
        DocumentType.CreditCard or DocumentType.Passport => TextRecognitionOptions.Alphanumeric,
        _ => TextRecognitionOptions.Document
    };

    async Task<ExtractedDocument> ExtractLicenseAsync(IReadOnlyList<byte[]> pages, CancellationToken ct)
    {
        if (!barcodeReader.IsSupported)
            throw new PlatformNotSupportedException("On-device barcode reading is not available on this platform.");

        // Use the first page whose PDF417 decodes as AAMVA (the barcode is usually on the license back).
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();
            var barcodes = await barcodeReader.ReadAsync(page, ct).ConfigureAwait(false);
            foreach (var barcode in barcodes.Where(b => b.Format == BarcodeFormat.Pdf417))
            {
                var license = AamvaParser.TryParse(barcode.Value);
                if (license is not null)
                    return new ExtractedDocument { Type = DocumentType.DriversLicense, License = license, RawText = barcode.Value };
            }
        }
        return new ExtractedDocument { Type = DocumentType.DriversLicense };
    }

    /// <summary>
    /// Parse, then enrich with detected entities. The detector runs over the same OCR text and only ever
    /// <i>adds</i>: it fills a date the type parser missed and attaches the entity list. It never overwrites
    /// a parsed field, so results on a platform without a detector are a strict subset, not a variant.
    /// </summary>
    ExtractedDocument Build(DocumentType type, string text)
    {
        var doc = BuildFromText(type, text);
        if (dataDetector is not { IsSupported: true } || text.Length == 0)
            return doc;

        var entities = dataDetector.Detect(text);
        if (entities.Count == 0)
            return doc;

        // Apple's detector handles locale-specific and relative date phrasing the managed regex doesn't, so
        // it's a good fallback — but only a fallback. Preferring it outright would need measuring against
        // real receipts first: the *first* date on a receipt isn't reliably the transaction date.
        var firstDate = entities
            .Where(e => e.Kind == DetectedEntityKind.Date && e.Date is not null)
            .Select(e => DateOnly.FromDateTime(e.Date!.Value.Date))
            .FirstOrDefault();
        var detected = firstDate == default ? (DateOnly?)null : firstDate;

        return new ExtractedDocument
        {
            Type = doc.Type,
            RawText = doc.RawText,
            Entities = entities,
            Receipt = doc.Receipt is { Date: null } r && detected is not null ? r with { Date = detected } : doc.Receipt,
            Invoice = doc.Invoice is { InvoiceDate: null } i && detected is not null ? i with { InvoiceDate = detected } : doc.Invoice,
            License = doc.License,
            Passport = doc.Passport,
            CreditCard = doc.CreditCard
        };
    }

    static ExtractedDocument BuildFromText(DocumentType type, string text) => type switch
    {
        DocumentType.Receipt => new ExtractedDocument { Type = type, RawText = text, Receipt = ReceiptParser.Parse(text) },
        DocumentType.Invoice => new ExtractedDocument { Type = type, RawText = text, Invoice = InvoiceParser.Parse(text) },
        DocumentType.Passport => new ExtractedDocument { Type = type, RawText = text, Passport = MrzParser.TryParse(text) },
        DocumentType.CreditCard => new ExtractedDocument { Type = type, RawText = text, CreditCard = CreditCardParser.TryParse(text) },
        _ => new ExtractedDocument { Type = type, RawText = text }
    };
}

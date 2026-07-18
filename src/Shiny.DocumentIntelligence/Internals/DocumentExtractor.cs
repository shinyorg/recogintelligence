namespace Shiny.DocumentIntelligence;

/// <summary>
/// Default <see cref="IDocumentExtractor"/>: cross-platform glue with no native code. It routes each
/// <see cref="DocumentType"/> to the right on-device primitive — OCR via <see cref="ITextRecognizer"/> for
/// receipts/invoices/passports, PDF417 decode via <see cref="IBarcodeReader"/> for licenses — and then
/// hands the result to the matching pure-C# parser. The native bits are injected, so this class is the same
/// on every platform and is fully unit-testable with fakes.
/// </summary>
public class DocumentExtractor(ITextRecognizer textRecognizer, IBarcodeReader barcodeReader) : IDocumentExtractor
{
    public async Task<ExtractedDocument> ExtractAsync(byte[] imageData, DocumentType type, CancellationToken cancellationToken = default)
    {
        if (type == DocumentType.DriversLicense)
            return await ExtractLicenseAsync([imageData], cancellationToken).ConfigureAwait(false);

        var text = await this.RecognizeAsync(imageData, cancellationToken).ConfigureAwait(false);
        return BuildFromText(type, text.FullText);
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
            var recognized = await this.RecognizeAsync(page, cancellationToken).ConfigureAwait(false);
            if (recognized.FullText.Length > 0)
                texts.Add(recognized.FullText);
        }
        return BuildFromText(type, string.Join('\n', texts));
    }

    async Task<RecognizedText> RecognizeAsync(byte[] image, CancellationToken ct)
    {
        if (!textRecognizer.IsSupported)
            throw new PlatformNotSupportedException("On-device text recognition is not available on this platform.");
        return await textRecognizer.RecognizeAsync(image, ct).ConfigureAwait(false);
    }

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

    static ExtractedDocument BuildFromText(DocumentType type, string text) => type switch
    {
        DocumentType.Receipt => new ExtractedDocument { Type = type, RawText = text, Receipt = ReceiptParser.Parse(text) },
        DocumentType.Invoice => new ExtractedDocument { Type = type, RawText = text, Invoice = InvoiceParser.Parse(text) },
        DocumentType.Passport => new ExtractedDocument { Type = type, RawText = text, Passport = MrzParser.TryParse(text) },
        _ => new ExtractedDocument { Type = type, RawText = text }
    };
}

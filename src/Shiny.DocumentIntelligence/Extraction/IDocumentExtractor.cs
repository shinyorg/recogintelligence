namespace Shiny.DocumentIntelligence;

/// <summary>
/// Turns scanned page images into structured data. This is the second half of the pipeline: the
/// <see cref="IDocumentScanner"/> captures clean page images, then this extracts fields from them on-device
/// via <see cref="ITextRecognizer"/> (receipts/invoices/passports) and <see cref="IBarcodeReader"/> (licenses).
/// Resolved from DI by <see cref="DocumentIntelligenceServiceCollectionExtensions.AddDocumentIntelligence"/>.
/// </summary>
public interface IDocumentExtractor
{
    /// <summary>Extract structured data from a single page image (PNG/JPEG bytes).</summary>
    Task<ExtractedDocument> ExtractAsync(byte[] imageData, DocumentType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract from a completed scan. Multi-page documents (invoices, passport data page) are handled by
    /// concatenating recognized text across pages; the license path scans each page for a PDF417 and uses
    /// the first that decodes.
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(DocumentScanResult scan, DocumentType type, CancellationToken cancellationToken = default);
}

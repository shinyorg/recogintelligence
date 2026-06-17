namespace Shiny.DocumentIntelligence;

/// <summary>The outcome of a scan: the captured pages and (optionally) a PDF, or a cancellation.</summary>
public class DocumentScanResult
{
    DocumentScanResult(bool cancelled, IReadOnlyList<DocumentScannedPage> pages, byte[]? pdf)
    {
        this.IsCancelled = cancelled;
        this.Pages = pages;
        this.Pdf = pdf;
    }

    /// <summary>True when the user dismissed the scanner without finishing. <see cref="Pages"/> is then empty.</summary>
    public bool IsCancelled { get; }

    /// <summary>One entry per scanned page, in order.</summary>
    public IReadOnlyList<DocumentScannedPage> Pages { get; }

    /// <summary>A multi-page PDF of the scan when <see cref="DocumentScanFormats.Pdf"/> was requested and the platform produced one (Android); otherwise null.</summary>
    public byte[]? Pdf { get; }

    /// <summary>A user-cancelled result.</summary>
    public static DocumentScanResult Cancelled { get; } = new(true, [], null);

    /// <summary>A completed result with the given pages and optional PDF.</summary>
    public static DocumentScanResult Completed(IReadOnlyList<DocumentScannedPage> pages, byte[]? pdf = null) =>
        new(false, pages, pdf);
}

namespace Shiny.DocumentIntelligence;

/// <summary>No-op scanner for platforms without a native document scanner (bare net10.0, Windows).</summary>
public class DocumentScanner : IDocumentScanner
{
    public bool IsSupported => false;

    public Task<DocumentScanResult> ScanAsync(DocumentScanRequest? request = null, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("Document scanning is not supported on this platform.");
}

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Launches the platform's native document scanner and returns the captured page(s). This is a
/// modal flow (it takes over the screen) — it is not a live frame analyzer.
/// </summary>
/// <remarks>
/// Platform behavior:
/// <list type="bullet">
/// <item>iOS / Mac Catalyst — VisionKit <c>VNDocumentCameraViewController</c> (auto edge-detect, perspective correct, multi-page).</item>
/// <item>Android — ML Kit <c>GmsDocumentScanning</c> (edge detect, crop, enhance; can also emit a PDF).</item>
/// <item>macOS (AppKit) — no first-party document camera, so the user picks image(s) and Vision
/// <c>VNDetectDocumentSegmentationRequest</c> + Core Image perspective correction deskews them.</item>
/// <item>Everything else (bare net10.0, Windows) — unsupported; <see cref="ScanAsync"/> throws.</item>
/// </list>
/// </remarks>
public interface IDocumentScanner
{
    /// <summary>True when the current platform/OS version can scan. Gate your UI on this.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Present the scanner and await the result. The returned task completes when the user finishes or
    /// cancels (<see cref="DocumentScanResult.IsCancelled"/>). Throws <see cref="PlatformNotSupportedException"/>
    /// when <see cref="IsSupported"/> is false.
    /// </summary>
    Task<DocumentScanResult> ScanAsync(DocumentScanRequest? request = null, CancellationToken cancellationToken = default);
}

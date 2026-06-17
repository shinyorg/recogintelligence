namespace Shiny.DocumentIntelligence;

/// <summary>A single scanned, deskewed page as encoded image bytes.</summary>
/// <param name="ImageData">The encoded image (PNG or JPEG per <paramref name="MimeType"/>).</param>
/// <param name="MimeType">The image encoding, e.g. <c>image/png</c> or <c>image/jpeg</c>.</param>
public record DocumentScannedPage(byte[] ImageData, string MimeType);

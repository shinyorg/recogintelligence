namespace Shiny.DocumentIntelligence;

/// <summary>
/// On-device barcode/QR decoding over a page image. Backed by the platform's native scanner
/// (Apple Vision <c>VNDetectBarcodesRequest</c>, Android ML Kit Barcode Scanning); a throwing stub
/// where neither exists. The driver's-license path needs <see cref="BarcodeFormat.Pdf417"/>.
/// </summary>
public interface IBarcodeReader
{
    /// <summary>True when the current platform can decode barcodes. Throwing stub platforms report false.</summary>
    bool IsSupported { get; }

    /// <summary>Decode every barcode found in an encoded image (PNG/JPEG bytes).</summary>
    Task<IReadOnlyList<Barcode>> ReadAsync(byte[] imageData, CancellationToken cancellationToken = default);
}

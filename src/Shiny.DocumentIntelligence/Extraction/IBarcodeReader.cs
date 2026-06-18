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

/// <summary>A decoded barcode.</summary>
/// <param name="Value">The decoded payload as text. For PDF417 on a US/CA license this is the raw AAMVA blob.</param>
/// <param name="Format">The symbology.</param>
public record Barcode(string Value, BarcodeFormat Format);

/// <summary>Barcode symbologies we surface. Mirrors the subset both Vision and ML Kit decode.</summary>
public enum BarcodeFormat
{
    Unknown,
    QrCode,
    Pdf417,
    Aztec,
    DataMatrix,
    Code128,
    Code39,
    Ean13,
    Ean8,
    UpcA,
    UpcE
}

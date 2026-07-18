namespace Shiny.DocumentIntelligence;

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

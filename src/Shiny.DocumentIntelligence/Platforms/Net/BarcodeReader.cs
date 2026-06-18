namespace Shiny.DocumentIntelligence;

/// <summary>No-op barcode reader for platforms without a native decoder (bare net10.0, Windows).</summary>
public class BarcodeReader : IBarcodeReader
{
    public bool IsSupported => false;

    public Task<IReadOnlyList<Barcode>> ReadAsync(byte[] imageData, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("On-device barcode reading is not supported on this platform.");
}

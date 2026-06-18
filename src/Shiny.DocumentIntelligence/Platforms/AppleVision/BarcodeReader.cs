using Foundation;
using ImageIO;
using Vision;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Apple Vision barcode decoding (<see cref="VNDetectBarcodesRequest"/>), shared by iOS, Mac Catalyst, and
/// macOS. The driver's-license path needs PDF417; Vision decodes that plus the usual 1D/2D symbologies.
/// </summary>
public class BarcodeReader : IBarcodeReader
{
    public bool IsSupported =>
        OperatingSystem.IsIOSVersionAtLeast(13) ||
        OperatingSystem.IsMacCatalystVersionAtLeast(13) ||
        OperatingSystem.IsMacOSVersionAtLeast(10, 15);

    public Task<IReadOnlyList<Barcode>> ReadAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        if (!this.IsSupported)
            throw new PlatformNotSupportedException("Apple Vision barcode detection requires iOS/Mac Catalyst 13 or macOS 10.15+.");

        ArgumentNullException.ThrowIfNull(imageData);

        return Task.Run<IReadOnlyList<Barcode>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var data = NSData.FromArray(imageData);
            using var source = CGImageSource.FromData(data);
            using var cgImage = source?.CreateImage(0, null!);
            if (cgImage is null)
                return [];

            // Null completion handler: results are read synchronously from the request after Perform.
            using var request = new VNDetectBarcodesRequest(completionHandler: null);
            using var handler = new VNImageRequestHandler(cgImage, new NSDictionary());
            if (!handler.Perform([request], out var error) || error is not null)
                return [];

            var observations = request.GetResults<VNBarcodeObservation>();
            if (observations is null)
                return [];

            var results = new List<Barcode>(observations.Length);
            foreach (var obs in observations)
            {
                var value = obs.PayloadStringValue;
                if (!String.IsNullOrEmpty(value))
                    results.Add(new Barcode(value, MapFormat(obs.Symbology)));
            }
            return results;
        }, cancellationToken);
    }

    // Map by the symbology's name to stay independent of exact binding enum spelling across SDK versions.
    static BarcodeFormat MapFormat(VNBarcodeSymbology symbology)
    {
        var name = symbology.ToString();
        if (name.Contains("Pdf417", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.Pdf417;
        if (name.Contains("QR", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.QrCode;
        if (name.Contains("Aztec", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.Aztec;
        if (name.Contains("DataMatrix", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.DataMatrix;
        if (name.Contains("Code128", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.Code128;
        if (name.Contains("Code39", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.Code39;
        if (name.Contains("Ean13", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.Ean13;
        if (name.Contains("Ean8", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.Ean8;
        if (name.Contains("Upce", StringComparison.OrdinalIgnoreCase)) return BarcodeFormat.UpcE;
        return BarcodeFormat.Unknown;
    }
}

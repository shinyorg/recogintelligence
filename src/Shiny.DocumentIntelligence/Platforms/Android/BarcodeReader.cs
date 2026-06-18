using Android.Graphics;
using Android.Runtime;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Common;
using JavaList = Java.Util.IList;
using MlBarcode = Xamarin.Google.MLKit.Vision.Barcode.Common.Barcode;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Android barcode decoding backed by ML Kit Barcode Scanning (binds <c>play-services-mlkit-barcode-scanning</c>).
/// Requests all formats (PDF417 included) so the same reader serves the license path and general QR/1D needs.
/// </summary>
public class BarcodeReader : IBarcodeReader
{
    public bool IsSupported => true;

    public async Task<IReadOnlyList<Barcode>> ReadAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = BitmapFactory.DecodeByteArray(imageData, 0, imageData.Length);
        if (bitmap is null)
            return [];

        var image = InputImage.FromBitmap(bitmap, 0);
        using var options = new BarcodeScannerOptions.Builder()
            .SetBarcodeFormats(MlBarcode.FormatAllFormats)
            .Build();
        var scanner = BarcodeScanning.GetClient(options);

        var resultObj = await scanner.Process(image).AsAsync().ConfigureAwait(false);
        var list = resultObj?.JavaCast<JavaList>();
        if (list is null)
            return [];

        var count = list.Size();
        var results = new List<Barcode>(count);
        for (var i = 0; i < count; i++)
        {
            if (list.Get(i) is not MlBarcode mlb)
                continue;
            var value = mlb.RawValue;
            if (!String.IsNullOrEmpty(value))
                results.Add(new Barcode(value, MapFormat(mlb.Format)));
        }
        return results;
    }

    // ML Kit format constants (Barcode.FORMAT_*) — if/else rather than a switch since the binding exposes
    // them as static fields, not compile-time constants.
    static BarcodeFormat MapFormat(int format)
    {
        if (format == MlBarcode.FormatPdf417) return BarcodeFormat.Pdf417;
        if (format == MlBarcode.FormatQrCode) return BarcodeFormat.QrCode;
        if (format == MlBarcode.FormatAztec) return BarcodeFormat.Aztec;
        if (format == MlBarcode.FormatDataMatrix) return BarcodeFormat.DataMatrix;
        if (format == MlBarcode.FormatCode128) return BarcodeFormat.Code128;
        if (format == MlBarcode.FormatCode39) return BarcodeFormat.Code39;
        if (format == MlBarcode.FormatEan13) return BarcodeFormat.Ean13;
        if (format == MlBarcode.FormatEan8) return BarcodeFormat.Ean8;
        if (format == MlBarcode.FormatUpcA) return BarcodeFormat.UpcA;
        if (format == MlBarcode.FormatUpcE) return BarcodeFormat.UpcE;
        return BarcodeFormat.Unknown;
    }
}

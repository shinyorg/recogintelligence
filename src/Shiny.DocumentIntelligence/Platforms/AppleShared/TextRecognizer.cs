using CoreGraphics;
using Foundation;
using ImageIO;
using Vision;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Apple Vision OCR (<see cref="VNRecognizeTextRequest"/>), shared by iOS, Mac Catalyst, and macOS. Images
/// are decoded via ImageIO's <see cref="CGImageSource"/> so there's no UIKit/AppKit split. Observations are
/// returned in reading order (top-to-bottom) — important for the passport MRZ.
/// </summary>
public class TextRecognizer : ITextRecognizer
{
    public bool IsSupported =>
        OperatingSystem.IsIOSVersionAtLeast(13) ||
        OperatingSystem.IsMacCatalystVersionAtLeast(13) ||
        OperatingSystem.IsMacOSVersionAtLeast(10, 15);

    public Task<RecognizedText> RecognizeAsync(byte[] imageData, TextRecognitionOptions options, CancellationToken cancellationToken = default)
    {
        if (!this.IsSupported)
            throw new PlatformNotSupportedException("Apple Vision text recognition requires iOS/Mac Catalyst 13 or macOS 10.15+.");

        ArgumentNullException.ThrowIfNull(imageData);

        // Vision's Perform is synchronous and CPU-bound — keep it off the calling thread.
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var data = NSData.FromArray(imageData);
            using var source = CGImageSource.FromData(data);
            using var cgImage = source?.CreateImage(0, null!);
            if (cgImage is null)
                return RecognizedText.Empty;

            // Null completion handler: results are read synchronously from the request after Perform.
            using var request = new VNRecognizeTextRequest(completionHandler: null)
            {
                RecognitionLevel = VNRequestTextRecognitionLevel.Accurate,
                UsesLanguageCorrection = options.UseLanguageCorrection
            };
            if (options.MinimumTextHeight > 0f)
                request.MinimumTextHeight = options.MinimumTextHeight;

            using var handler = new VNImageRequestHandler(cgImage, new NSDictionary());
            if (!handler.Perform([request], out var error) || error is not null)
                return RecognizedText.Empty;

            var observations = request.GetResults<VNRecognizedTextObservation>();
            if (observations is null || observations.Length == 0)
                return RecognizedText.Empty;

            // Vision's origin is bottom-left, so descending Y is top-to-bottom reading order.
            var lines = observations
                .OrderByDescending(o => o.BoundingBox.Y)
                .Select(o =>
                {
                    var candidate = o.TopCandidates(1)?.FirstOrDefault();
                    return candidate is null ? null : new RecognizedLine(candidate.String, candidate.Confidence);
                })
                .Where(l => l is not null && l.Text.Length > 0)
                .Select(l => l!)
                .ToList();

            return RecognizedText.FromLines(lines);
        }, cancellationToken);
    }
}

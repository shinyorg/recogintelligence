using Android.Graphics;
using Android.Runtime;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Android OCR backed by ML Kit Text Recognition (binds <c>play-services-mlkit-text-recognition</c>). Decodes
/// the page bytes to a <see cref="Bitmap"/>, runs the on-device Latin recognizer, and flattens the
/// block/line structure to <see cref="RecognizedText"/>. Each line keeps its bounding box, so
/// <see cref="RecognizedText.FromLines"/> imposes reading order (ML Kit's block order isn't it) and groups
/// lines split across a column gap back into one row.
/// </summary>
public class TextRecognizer : ITextRecognizer
{
    public bool IsSupported => true;

    public async Task<RecognizedText> RecognizeAsync(byte[] imageData, TextRecognitionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        cancellationToken.ThrowIfCancellationRequested();

        using var bitmap = BitmapFactory.DecodeByteArray(imageData, 0, imageData.Length);
        if (bitmap is null)
            return RecognizedText.Empty;

        var image = InputImage.FromBitmap(bitmap, 0);
        var recognizer = TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions!);

        var resultObj = await recognizer.Process(image).AsAsync().ConfigureAwait(false);
        var text = resultObj?.JavaCast<Text>();
        if (text?.TextBlocks is null)
            return RecognizedText.Empty;

        // Captured before the bitmap goes out of scope: ML Kit reports pixel rects against this image.
        float imageWidth = bitmap.Width, imageHeight = bitmap.Height;

        var lines = new List<RecognizedLine>();
        foreach (var block in text.TextBlocks)
        {
            if (block is not Text.TextBlock tb || tb.Lines is null)
                continue;
            foreach (var line in tb.Lines)
            {
                if (line is Text.Line l && !String.IsNullOrEmpty(l.Text))
                    lines.Add(new RecognizedLine(l.Text, l.Confidence, ToBounds(l.BoundingBox, imageWidth, imageHeight)));
            }
        }
        return RecognizedText.FromLines(lines);
    }

    /// <summary>ML Kit reports pixel rects against the input bitmap; <see cref="TextBounds"/> is normalized.</summary>
    static TextBounds? ToBounds(Rect? rect, float imageWidth, float imageHeight) =>
        rect is null || imageWidth <= 0f || imageHeight <= 0f
            ? null
            : new TextBounds(rect.Left / imageWidth, rect.Top / imageHeight, rect.Width() / imageWidth, rect.Height() / imageHeight);
}

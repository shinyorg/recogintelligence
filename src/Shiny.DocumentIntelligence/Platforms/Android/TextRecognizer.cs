using Android.Graphics;
using Android.Runtime;
using Xamarin.Google.MLKit.Vision.Common;
using Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// Android OCR backed by ML Kit Text Recognition (binds <c>play-services-mlkit-text-recognition</c>). Decodes
/// the page bytes to a <see cref="Bitmap"/>, runs the on-device Latin recognizer, and flattens the
/// block/line structure to <see cref="RecognizedText"/> in reading order.
/// </summary>
public class TextRecognizer : ITextRecognizer
{
    public bool IsSupported => true;

    public async Task<RecognizedText> RecognizeAsync(byte[] imageData, CancellationToken cancellationToken = default)
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

        var lines = new List<RecognizedLine>();
        foreach (var block in text.TextBlocks)
        {
            if (block is not Text.TextBlock tb || tb.Lines is null)
                continue;
            foreach (var line in tb.Lines)
            {
                if (line is Text.Line l && !String.IsNullOrEmpty(l.Text))
                    lines.Add(new RecognizedLine(l.Text, l.Confidence));
            }
        }
        return RecognizedText.FromLines(lines);
    }
}

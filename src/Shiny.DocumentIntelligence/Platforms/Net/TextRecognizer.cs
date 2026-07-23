namespace Shiny.DocumentIntelligence;

/// <summary>No-op OCR for platforms without a native text recognizer (bare net10.0, Windows).</summary>
public class TextRecognizer : ITextRecognizer
{
    public bool IsSupported => false;

    public Task<RecognizedText> RecognizeAsync(byte[] imageData, TextRecognitionOptions options, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException("On-device text recognition is not supported on this platform.");
}

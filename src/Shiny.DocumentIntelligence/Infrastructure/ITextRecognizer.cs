namespace Shiny.DocumentIntelligence;

/// <summary>
/// On-device OCR over a page image. Backed by the platform's native text recognizer
/// (Apple Vision <c>VNRecognizeTextRequest</c>, Android ML Kit Text Recognition); a throwing stub
/// where neither exists. Resolved from DI by <see cref="DocumentIntelligenceServiceCollectionExtensions.AddDocumentIntelligence"/>.
/// </summary>
public interface ITextRecognizer
{
    /// <summary>True when the current platform can run OCR. Throwing stub platforms report false.</summary>
    bool IsSupported { get; }

    /// <summary>Recognize text in an encoded image (PNG/JPEG bytes — exactly what <see cref="DocumentScannedPage.ImageData"/> holds).</summary>
    Task<RecognizedText> RecognizeAsync(byte[] imageData, CancellationToken cancellationToken = default);
}

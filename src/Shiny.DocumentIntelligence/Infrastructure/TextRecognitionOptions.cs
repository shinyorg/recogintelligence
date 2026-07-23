namespace Shiny.DocumentIntelligence;

/// <summary>
/// Tuning passed to <see cref="ITextRecognizer"/>. Defaults suit prose; short alphanumeric strings need
/// different settings.
/// </summary>
/// <param name="UseLanguageCorrection">
/// Whether the engine may apply its language model to the result. Excellent on prose — it fixes genuine
/// misreads using surrounding words — and actively harmful on a card number or serial, where there is no
/// linguistic context and "correction" can only move a digit toward something wrong.
/// </param>
/// <param name="MinimumTextHeight">
/// Smallest text to consider, as a fraction of image height. <c>0</c> leaves the platform default. Lowering
/// it finds smaller text at the cost of more false positives and slower recognition.
/// </param>
public record TextRecognitionOptions(
    bool UseLanguageCorrection = true,
    float MinimumTextHeight = 0f
)
{
    /// <summary>Defaults for prose-heavy documents: receipts, invoices, letters.</summary>
    public static readonly TextRecognitionOptions Document = new();

    /// <summary>
    /// For short alphanumeric strings with no linguistic context — card numbers, MRZ lines, serials.
    /// Language correction is off, and the height floor is lowered because these are often set small.
    /// </summary>
    public static readonly TextRecognitionOptions Alphanumeric = new(UseLanguageCorrection: false, MinimumTextHeight: 0.01f);
}

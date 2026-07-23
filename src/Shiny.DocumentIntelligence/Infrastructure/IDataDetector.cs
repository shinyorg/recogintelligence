namespace Shiny.DocumentIntelligence;

/// <summary>
/// Finds dates, addresses, phone numbers and links inside already-recognized text.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists because Apple ships a genuinely good one — <c>NSDataDetector</c>, the same engine behind
/// tappable dates and addresses in Mail and Messages — and it beats hand-rolled regex on real-world text:
/// it handles locale-specific date formats, relative phrasing, and multi-line addresses that a pattern
/// match won't. It runs on a <see cref="string"/> rather than an image, so it sits <i>after</i>
/// <see cref="ITextRecognizer"/> and composes with any OCR source.
/// </para>
/// <para>
/// Nothing depends on it: where it isn't available <see cref="IsSupported"/> is false, <see cref="Detect"/>
/// returns empty, and the managed parsers carry on exactly as before.
/// </para>
/// </remarks>
public interface IDataDetector
{
    /// <summary>Whether this platform has a data detector. False means <see cref="Detect"/> always returns empty.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Locate entities in <paramref name="text"/>, in the order they appear. Returns empty when nothing is
    /// found or the platform has no detector — it never throws, so callers can treat it as pure enrichment.
    /// </summary>
    IReadOnlyList<DetectedEntity> Detect(string text);
}

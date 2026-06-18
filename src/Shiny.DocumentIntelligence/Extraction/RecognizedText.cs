namespace Shiny.DocumentIntelligence;

/// <summary>The result of running OCR over a page image.</summary>
/// <param name="FullText">All recognized text joined with newlines, in reading order.</param>
/// <param name="Lines">The recognized lines, top-to-bottom, left-to-right.</param>
public record RecognizedText(string FullText, IReadOnlyList<RecognizedLine> Lines)
{
    /// <summary>An empty result (nothing recognized).</summary>
    public static RecognizedText Empty { get; } = new(string.Empty, []);

    /// <summary>Builds a <see cref="RecognizedText"/> from lines, composing <see cref="FullText"/> for you.</summary>
    public static RecognizedText FromLines(IReadOnlyList<RecognizedLine> lines) =>
        new(string.Join('\n', lines.Select(l => l.Text)), lines);
}

/// <summary>A single recognized line of text and the platform's confidence in it.</summary>
/// <param name="Text">The line text.</param>
/// <param name="Confidence">0..1 recognition confidence, or null when the platform doesn't report it.</param>
public record RecognizedLine(string Text, float? Confidence = null);

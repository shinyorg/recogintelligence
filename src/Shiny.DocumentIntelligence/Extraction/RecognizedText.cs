namespace Shiny.DocumentIntelligence;

/// <summary>The result of running OCR over a page image.</summary>
/// <param name="FullText">The <see cref="Rows"/> joined with newlines — one line of text per visual row.</param>
/// <param name="Lines">
/// The raw fragments the engine returned, in reading order. An engine splits at large whitespace gaps, so a
/// two-column receipt row arrives here as two fragments ("TOTAL" and "24.99"); use <see cref="Rows"/> when you
/// want what a human sees as one line.
/// </param>
public record RecognizedText(string FullText, IReadOnlyList<RecognizedLine> Lines)
{
    /// <summary>
    /// The fragments grouped into visual rows, top-to-bottom, each row's fragments joined left-to-right. This
    /// is what the parsers read: a label and its amount are only on the same row after grouping. Falls back to
    /// <see cref="Lines"/> verbatim when the engine reports no geometry.
    /// </summary>
    public IReadOnlyList<RecognizedLine> Rows { get; init; } = Lines;

    /// <summary>An empty result (nothing recognized).</summary>
    public static RecognizedText Empty { get; } = new(string.Empty, []);

    /// <summary>
    /// Builds a <see cref="RecognizedText"/> from raw engine fragments: orders them into reading order, groups
    /// them into <see cref="Rows"/>, and composes <see cref="FullText"/> from those rows.
    /// </summary>
    public static RecognizedText FromLines(IReadOnlyList<RecognizedLine> lines)
    {
        var ordered = TextLayout.ReadingOrder(lines);
        var rows = TextLayout.GroupRows(ordered);
        return new RecognizedText(string.Join('\n', rows.Select(r => r.Text)), ordered) { Rows = rows };
    }
}

/// <summary>A single recognized fragment of text and the platform's confidence in it.</summary>
/// <param name="Text">The fragment text.</param>
/// <param name="Confidence">0..1 recognition confidence, or null when the platform doesn't report it.</param>
/// <param name="Bounds">Where it sits on the page, or null when the platform doesn't report it.</param>
public record RecognizedLine(string Text, float? Confidence = null, TextBounds? Bounds = null);

/// <summary>
/// A rectangle on the page, normalized to 0..1 of the image's width and height, with a <b>top-left</b> origin
/// (so increasing <paramref name="Y"/> moves down the page). Platforms that use another convention — Vision's
/// bottom-left origin, ML Kit's pixel rects — convert on the way in, so layout code has one coordinate space.
/// </summary>
public sealed record TextBounds(float X, float Y, float Width, float Height)
{
    /// <summary>The right edge.</summary>
    public float Right => this.X + this.Width;

    /// <summary>The bottom edge.</summary>
    public float Bottom => this.Y + this.Height;

    /// <summary>The vertical midpoint — what row grouping compares.</summary>
    public float CenterY => this.Y + (this.Height / 2f);

    /// <summary>The smallest rectangle containing both this and <paramref name="other"/>.</summary>
    public TextBounds Union(TextBounds other)
    {
        var x = Math.Min(this.X, other.X);
        var y = Math.Min(this.Y, other.Y);
        return new TextBounds(x, y, Math.Max(this.Right, other.Right) - x, Math.Max(this.Bottom, other.Bottom) - y);
    }
}

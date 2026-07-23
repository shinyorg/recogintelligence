namespace Shiny.DocumentIntelligence;

/// <summary>
/// Reassembles the fragments an OCR engine returns into the rows a human sees.
/// </summary>
/// <remarks>
/// Both engines split text at large whitespace gaps, so a receipt's "TOTAL ......... 24.99" comes back as two
/// separate observations — the label and the amount are never in the same string. Every parser here is
/// keyword-anchored ("the amount on the line that says total"), so without this pass they match nothing and
/// fall through to their fallbacks. Grouping by vertical position puts the two back together.
///
/// The rule is deliberately one-dimensional: fragments belong to the same row when their vertical centres are
/// close relative to the <i>median</i> fragment height. Median rather than mean because a receipt's merchant
/// name is several times the height of its line items and would otherwise stretch the tolerance until adjacent
/// rows merged.
/// </remarks>
static class TextLayout
{
    /// <summary>
    /// How far a fragment's vertical centre may sit from a row's centre, as a fraction of the median fragment
    /// height, and still join that row. Comfortably below the ~1.2x spacing of consecutive text rows, so a
    /// passport's two MRZ lines stay separate, while a column of amounts still lands on its labels.
    /// </summary>
    const float RowTolerance = 0.6f;

    /// <summary>Orders fragments top-to-bottom, then left-to-right. Input order is kept when geometry is missing.</summary>
    public static IReadOnlyList<RecognizedLine> ReadingOrder(IReadOnlyList<RecognizedLine> lines) =>
        lines.Count < 2 || !AllPositioned(lines)
            ? lines
            : lines.OrderBy(l => l.Bounds!.CenterY).ThenBy(l => l.Bounds!.X).ToList();

    /// <summary>
    /// Groups reading-ordered fragments into visual rows. Returns the input untouched when any fragment lacks
    /// geometry — a partially positioned set can't be grouped safely, and a custom <see cref="ITextRecognizer"/>
    /// that reports none keeps exactly its old behaviour.
    /// </summary>
    public static IReadOnlyList<RecognizedLine> GroupRows(IReadOnlyList<RecognizedLine> ordered)
    {
        if (ordered.Count < 2 || !AllPositioned(ordered))
            return ordered;

        var tolerance = Median(ordered.Select(l => l.Bounds!.Height).ToList()) * RowTolerance;

        var rows = new List<List<RecognizedLine>>();
        var center = 0f;
        foreach (var line in ordered)
        {
            var y = line.Bounds!.CenterY;
            if (rows.Count > 0 && Math.Abs(y - center) <= tolerance)
            {
                var row = rows[^1];
                row.Add(line);
                // Track the running mean so a slightly skewed row doesn't drift away from its first member.
                center = (float)row.Average(l => l.Bounds!.CenterY);
            }
            else
            {
                rows.Add([line]);
                center = y;
            }
        }
        return rows.Select(Merge).ToList();
    }

    static RecognizedLine Merge(List<RecognizedLine> row)
    {
        if (row.Count == 1)
            return row[0];

        var ordered = row.OrderBy(l => l.Bounds!.X).ToList();
        var confidences = ordered.Where(l => l.Confidence is not null).Select(l => l.Confidence!.Value).ToList();
        return new RecognizedLine(
            string.Join(' ', ordered.Select(l => l.Text)),
            // A row is only as trustworthy as its weakest fragment.
            confidences.Count == 0 ? null : confidences.Min(),
            ordered.Select(l => l.Bounds!).Aggregate((a, b) => a.Union(b))
        );
    }

    static bool AllPositioned(IReadOnlyList<RecognizedLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.Bounds is null)
                return false;
        }
        return true;
    }

    static float Median(List<float> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2f;
    }
}

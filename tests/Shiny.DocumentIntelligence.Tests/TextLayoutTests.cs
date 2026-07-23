using Xunit;
using Shiny.DocumentIntelligence;

namespace Shiny.DocumentIntelligence.Tests;

/// <summary>
/// Row grouping, exercised through the <see cref="RecognizedText.FromLines"/> seam every recognizer uses.
/// Bounds here are normalized with a top-left origin, exactly as the platform recognizers convert them.
/// </summary>
public class TextLayoutTests
{
    // A fragment of text on a page. Height defaults to a typical receipt line; rows sit ~0.04 apart.
    static RecognizedLine At(string text, float x, float y, float width = 0.2f, float height = 0.02f, float? confidence = null) =>
        new(text, confidence, new TextBounds(x, y, width, height));

    [Fact]
    public void ColumnsOnTheSameRow_AreJoined()
    {
        // What Vision actually returns for "TOTAL .......... 24.99": two observations, one per column.
        var text = RecognizedText.FromLines([
            At("TOTAL", 0.05f, 0.500f),
            At("24.99", 0.80f, 0.502f)
        ]);

        Assert.Equal("TOTAL 24.99", text.FullText);
        Assert.Single(text.Rows);
        // The raw fragments are still there for anyone who wants them.
        Assert.Equal(2, text.Lines.Count);
    }

    [Fact]
    public void SeparateRows_StaySeparate()
    {
        var text = RecognizedText.FromLines([
            At("SUBTOTAL", 0.05f, 0.50f),
            At("22.99", 0.80f, 0.50f),
            At("TOTAL", 0.05f, 0.56f),
            At("24.99", 0.80f, 0.56f)
        ]);

        Assert.Equal(["SUBTOTAL 22.99", "TOTAL 24.99"], text.Rows.Select(r => r.Text));
    }

    [Fact]
    public void Rows_ComeOutTopToBottom_AndFragmentsLeftToRight()
    {
        // Fed in deliberately scrambled: ML Kit's block order is not reading order.
        var text = RecognizedText.FromLines([
            At("9.99", 0.80f, 0.30f),
            At("BOTTOM", 0.05f, 0.90f),
            At("Coffee", 0.05f, 0.30f),
            At("TOP", 0.05f, 0.10f)
        ]);

        Assert.Equal(["TOP", "Coffee 9.99", "BOTTOM"], text.Rows.Select(r => r.Text));
    }

    [Fact]
    public void TallMerchantName_DoesNotStretchToleranceIntoNeighbouringRows()
    {
        // The median height (0.02) sets the tolerance, so the 0.08-high title can't drag its neighbours in.
        var text = RecognizedText.FromLines([
            At("ACME DELI", 0.05f, 0.02f, width: 0.6f, height: 0.08f),
            At("Sandwich", 0.05f, 0.20f),
            At("7.50", 0.80f, 0.20f),
            At("Coffee", 0.05f, 0.26f),
            At("2.25", 0.80f, 0.26f)
        ]);

        Assert.Equal(["ACME DELI", "Sandwich 7.50", "Coffee 2.25"], text.Rows.Select(r => r.Text));
    }

    [Fact]
    public void MrzLines_AreNotMergedIntoOne()
    {
        // The two TD3 lines are adjacent and the same height — the case grouping most needs to leave alone.
        const string l1 = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
        const string l2 = "L898902C36UTO7408122F1204159ZE184226B<<<<<10";
        var text = RecognizedText.FromLines([
            At(l1, 0.05f, 0.860f, width: 0.9f, height: 0.02f),
            At(l2, 0.05f, 0.895f, width: 0.9f, height: 0.02f)
        ]);

        Assert.Equal([l1, l2], text.Rows.Select(r => r.Text));
    }

    [Fact]
    public void SplitMrzLine_IsRejoined()
    {
        // Vision splits a long MRZ line at the filler run; grouping is what makes it 44 chars again.
        var text = RecognizedText.FromLines([
            At("L898902C36UTO7408122F1204159", 0.05f, 0.895f, width: 0.5f, height: 0.02f),
            At("ZE184226B<<<<<10", 0.60f, 0.895f, width: 0.3f, height: 0.02f)
        ]);

        Assert.Single(text.Rows);
        Assert.Equal("L898902C36UTO7408122F1204159 ZE184226B<<<<<10", text.Rows[0].Text);
    }

    [Fact]
    public void MergedRow_TakesTheWeakestConfidence_AndTheUnionOfBounds()
    {
        var text = RecognizedText.FromLines([
            At("TOTAL", 0.05f, 0.50f, width: 0.20f, height: 0.02f, confidence: 0.95f),
            At("24.99", 0.80f, 0.50f, width: 0.15f, height: 0.02f, confidence: 0.40f)
        ]);

        var row = Assert.Single(text.Rows);
        Assert.Equal(0.40f, row.Confidence);
        Assert.Equal(0.05f, row.Bounds!.X, 4);
        Assert.Equal(0.95f, row.Bounds.Right, 4);
    }

    [Fact]
    public void WithoutGeometry_OrderAndContentAreUntouched()
    {
        // A custom ITextRecognizer that reports no bounds keeps exactly its old behaviour.
        var text = RecognizedText.FromLines([
            new RecognizedLine("second"),
            new RecognizedLine("first")
        ]);

        Assert.Equal(["second", "first"], text.Rows.Select(r => r.Text));
        Assert.Equal("second\nfirst", text.FullText);
    }

    [Fact]
    public void PartialGeometry_IsNotGrouped()
    {
        // A mixed set can't be grouped safely — the unpositioned fragment has no row to belong to.
        var text = RecognizedText.FromLines([
            At("TOTAL", 0.05f, 0.50f),
            new RecognizedLine("24.99")
        ]);

        Assert.Equal(2, text.Rows.Count);
    }

    [Fact]
    public void GroupedRows_LetTheReceiptParserFindTheRealTotal()
    {
        // End to end: the point of the whole exercise. Before grouping, no line contained both "total" and an
        // amount, so ReceiptParser fell back to the largest number on the page — the pre-discount subtotal.
        var text = RecognizedText.FromLines([
            At("ACME DELI", 0.05f, 0.02f, width: 0.6f, height: 0.06f),
            At("Sandwich", 0.05f, 0.20f), At("7.50", 0.80f, 0.20f),
            At("SUBTOTAL", 0.05f, 0.40f), At("99.50", 0.80f, 0.40f),
            At("DISCOUNT", 0.05f, 0.46f), At("-75.00", 0.80f, 0.46f),
            At("TOTAL", 0.05f, 0.52f), At("24.50", 0.80f, 0.52f)
        ]);

        var receipt = ReceiptParser.Parse(text.FullText);

        Assert.Equal(24.50m, receipt.Total);
        Assert.Equal(99.50m, receipt.Subtotal);
    }
}

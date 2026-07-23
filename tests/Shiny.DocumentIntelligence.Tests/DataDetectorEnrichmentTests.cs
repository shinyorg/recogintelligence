using Xunit;

namespace Shiny.DocumentIntelligence.Tests;

/// <summary>
/// The detector is platform-native, so these cover the <b>contract</b> around it: that enrichment is purely
/// additive, and that a platform without a detector behaves exactly as before.
/// </summary>
public class DataDetectorEnrichmentTests
{
    sealed class FakeDetector(bool supported, params DetectedEntity[] entities) : IDataDetector
    {
        public bool IsSupported { get; } = supported;
        public IReadOnlyList<DetectedEntity> Detect(string text) => entities;
    }

    sealed class StubOcr(string text) : ITextRecognizer
    {
        public bool IsSupported => true;
        public Task<RecognizedText> RecognizeAsync(byte[] imageData, TextRecognitionOptions options, CancellationToken ct = default)
            => Task.FromResult(new RecognizedText(text, []));
    }

    sealed class NoBarcodes : IBarcodeReader
    {
        public bool IsSupported => true;
        public Task<IReadOnlyList<Barcode>> ReadAsync(byte[] imageData, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Barcode>>([]);
    }

    static readonly DetectedEntity DetectedDate =
        new(DetectedEntityKind.Date, "March 3, 2026", new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task EntitiesAreSurfaced()
    {
        var phone = new DetectedEntity(DetectedEntityKind.PhoneNumber, "+1 555-0100");
        var extractor = new DocumentExtractor(new StubOcr("CALL US"), new NoBarcodes(), new FakeDetector(true, phone));

        var doc = await extractor.ExtractAsync([1], DocumentType.Receipt);

        Assert.Single(doc.Entities);
        Assert.Equal(DetectedEntityKind.PhoneNumber, doc.Entities[0].Kind);
    }

    [Fact]
    public async Task UnsupportedDetector_ChangesNothing()
    {
        var ocr = new StubOcr("STORE\nTOTAL 12.34");
        var withOut = await new DocumentExtractor(ocr, new NoBarcodes()).ExtractAsync([1], DocumentType.Receipt);
        var withInert = await new DocumentExtractor(ocr, new NoBarcodes(), new FakeDetector(false, DetectedDate))
            .ExtractAsync([1], DocumentType.Receipt);

        Assert.Empty(withInert.Entities);
        Assert.Equal(withOut.Receipt?.Total, withInert.Receipt?.Total);
        Assert.Equal(withOut.Receipt?.Date, withInert.Receipt?.Date);
    }

    [Fact]
    public async Task DetectedDate_FillsAMissingReceiptDate()
    {
        // No date the managed regex can see, but the detector resolved one.
        var extractor = new DocumentExtractor(
            new StubOcr("CORNER STORE\nTOTAL 9.99"), new NoBarcodes(), new FakeDetector(true, DetectedDate));

        var doc = await extractor.ExtractAsync([1], DocumentType.Receipt);

        Assert.Equal(new DateOnly(2026, 3, 3), doc.Receipt?.Date);
    }

    [Fact]
    public async Task DetectedDate_NeverOverwritesAParsedDate()
    {
        // The receipt states 2024-01-31; the detector claims something else. The parser wins.
        var extractor = new DocumentExtractor(
            new StubOcr("CORNER STORE\n2024-01-31\nTOTAL 9.99"), new NoBarcodes(), new FakeDetector(true, DetectedDate));

        var doc = await extractor.ExtractAsync([1], DocumentType.Receipt);

        Assert.Equal(new DateOnly(2024, 1, 31), doc.Receipt?.Date);
    }

    [Fact]
    public async Task DetectedDate_FillsAMissingInvoiceDate()
    {
        var extractor = new DocumentExtractor(
            new StubOcr("ACME LTD\nINVOICE 42\nTOTAL 100.00"), new NoBarcodes(), new FakeDetector(true, DetectedDate));

        var doc = await extractor.ExtractAsync([1], DocumentType.Invoice);

        Assert.Equal(new DateOnly(2026, 3, 3), doc.Invoice?.InvoiceDate);
    }
}

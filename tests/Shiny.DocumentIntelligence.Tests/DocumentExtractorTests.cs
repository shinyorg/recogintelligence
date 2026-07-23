using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Shiny.DocumentIntelligence;

namespace Shiny.DocumentIntelligence.Tests;

public class DocumentExtractorTests
{
    static DocumentScanResult Scan(params string[] pageMarkers) =>
        DocumentScanResult.Completed(pageMarkers.Select(m => new DocumentScannedPage([(byte)m[0]], "image/png")).ToList());

    [Fact]
    public async Task Receipt_RoutesThroughOcrAndParser()
    {
        var ocr = new FakeTextRecognizer("DELI\nSandwich  7.50\nTotal  7.50\n");
        var extractor = new DocumentExtractor(ocr, FakeBarcodeReader.Empty);

        var result = await extractor.ExtractAsync([1, 2, 3], DocumentType.Receipt);

        Assert.Equal(DocumentType.Receipt, result.Type);
        Assert.NotNull(result.Receipt);
        Assert.Equal(7.50m, result.Receipt!.Total);
        Assert.True(result.HasStructuredData);
    }

    [Fact]
    public async Task License_RoutesThroughBarcodeReader_NotOcr()
    {
        const string aamva = "@\n\rANSI 636026080002DL00410288\nDLDAQX99\nDCSDOE\nDACJANE\nDBC2\r";
        var ocr = FakeTextRecognizer.Throwing; // OCR must NOT be touched on the license path
        var barcodes = new FakeBarcodeReader([new Barcode(aamva, BarcodeFormat.Pdf417)]);
        var extractor = new DocumentExtractor(ocr, barcodes);

        var result = await extractor.ExtractAsync(Scan("a"), DocumentType.DriversLicense);

        Assert.NotNull(result.License);
        Assert.Equal("JANE", result.License!.FirstName);
        Assert.Equal("DOE", result.License.LastName);
    }

    [Fact]
    public async Task License_IgnoresNonPdf417Barcodes()
    {
        var barcodes = new FakeBarcodeReader([new Barcode("https://qr.example", BarcodeFormat.QrCode)]);
        var extractor = new DocumentExtractor(FakeTextRecognizer.Throwing, barcodes);

        var result = await extractor.ExtractAsync(Scan("a"), DocumentType.DriversLicense);

        Assert.Null(result.License);
        Assert.False(result.HasStructuredData);
    }

    [Fact]
    public async Task MultiPageInvoice_ConcatenatesOcrAcrossPages()
    {
        var ocr = new FakeTextRecognizer(
            "ACME CORP\nInvoice Number: INV-7\n",
            "Widget  100.00\nTotal Due  100.00\n");
        var extractor = new DocumentExtractor(ocr, FakeBarcodeReader.Empty);

        var result = await extractor.ExtractAsync(Scan("a", "b"), DocumentType.Invoice);

        Assert.Equal("INV-7", result.Invoice!.InvoiceNumber);
        Assert.Equal(100.00m, result.Invoice.Total);
    }

    [Fact]
    public void AddDocumentIntelligence_RegistersTheWholePipeline()
    {
        var services = new ServiceCollection();
        services.AddDocumentIntelligence();
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IDocumentScanner>());
        Assert.NotNull(provider.GetService<ITextRecognizer>());
        Assert.NotNull(provider.GetService<IBarcodeReader>());
        Assert.NotNull(provider.GetService<IDocumentExtractor>());
    }

    // --- fakes ---

    sealed class FakeTextRecognizer(params string[] pageTexts) : ITextRecognizer
    {
        int call;
        public static FakeTextRecognizer Throwing => new() { throwOnUse = true };
        bool throwOnUse;

        public bool IsSupported => true;

        public Task<RecognizedText> RecognizeAsync(byte[] imageData, TextRecognitionOptions options, CancellationToken cancellationToken = default)
        {
            if (this.throwOnUse)
                throw new InvalidOperationException("OCR should not have been called on this path.");
            var text = pageTexts.Length == 0 ? string.Empty : pageTexts[Math.Min(this.call++, pageTexts.Length - 1)];
            return Task.FromResult(new RecognizedText(text, text.Split('\n').Where(l => l.Length > 0).Select(l => new RecognizedLine(l)).ToList()));
        }
    }

    sealed class FakeBarcodeReader(IReadOnlyList<Barcode> barcodes) : IBarcodeReader
    {
        public static FakeBarcodeReader Empty => new([]);
        public bool IsSupported => true;
        public Task<IReadOnlyList<Barcode>> ReadAsync(byte[] imageData, CancellationToken cancellationToken = default) =>
            Task.FromResult(barcodes);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shiny.DocumentIntelligence;

public static class DocumentIntelligenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the document-intelligence pipeline:
    /// <list type="bullet">
    /// <item><see cref="IDocumentScanner"/> — native capture (VisionKit iOS/Mac Catalyst, ML Kit Android, Vision segmentation macOS; throwing stub elsewhere).</item>
    /// <item><see cref="ITextRecognizer"/> — native OCR (Apple Vision / Android ML Kit; throwing stub elsewhere).</item>
    /// <item><see cref="IBarcodeReader"/> — native barcode/PDF417 decode (Apple Vision / Android ML Kit; throwing stub elsewhere).</item>
    /// <item><see cref="IDocumentExtractor"/> — cross-platform orchestrator turning scans into <see cref="ExtractedDocument"/>.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddDocumentIntelligence(this IServiceCollection services)
    {
        services.TryAddSingleton<IDocumentScanner, DocumentScanner>();
        services.TryAddSingleton<ITextRecognizer, TextRecognizer>();
        services.TryAddSingleton<IBarcodeReader, BarcodeReader>();
        services.TryAddSingleton<IDocumentExtractor, DocumentExtractor>();
        return services;
    }
}

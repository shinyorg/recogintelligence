using Shiny;
using Shiny.DocumentIntelligence;

namespace Sample.Features.Documents;

/// <summary>Registers the native modal document scanner + on-device extractor.</summary>
public class DocumentsModule : IMauiModule
{
    // Native document scanner (IDocumentScanner): VisionKit on iOS, ML Kit on Android; plus the extractor.
    public void Add(MauiAppBuilder builder) => builder.Services.AddDocumentIntelligence();

    public void Use(IPlatformApplication app) { }
}

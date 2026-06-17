using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shiny.DocumentIntelligence;

public static class DocumentIntelligenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDocumentScanner"/> with the platform-native implementation (VisionKit on
    /// iOS/Mac Catalyst, ML Kit on Android, Vision segmentation on macOS; a throwing stub elsewhere).
    /// </summary>
    public static IServiceCollection AddDocumentIntelligence(this IServiceCollection services)
    {
        services.TryAddSingleton<IDocumentScanner, DocumentScanner>();
        return services;
    }
}

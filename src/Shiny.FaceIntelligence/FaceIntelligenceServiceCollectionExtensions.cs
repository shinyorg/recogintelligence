using Microsoft.Extensions.DependencyInjection;

namespace Shiny.FaceIntelligence;

public static class FaceIntelligenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the face pipeline. Compose it inside <paramref name="build"/>: pick an embedder
    /// (e.g. <c>UseOnnxEmbedder</c> from Shiny.FaceIntelligence.Onnx) and a store (e.g. <c>UseSqliteStore</c>
    /// from Shiny.FaceIntelligence.DocumentDb.Sqlite), and set matching options via <c>builder.Options</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddFaceIntelligence(face =>
    /// {
    ///     face.Options.MaxDistance = 0.6f;
    ///     face.UseOnnxEmbedder(o => o.ModelBytesProvider = LoadModel);
    ///     face.UseSqliteStore(o => { o.ConnectionString = "..."; o.VectorExtensionPath = "vec0"; });
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddFaceIntelligence(
        this IServiceCollection services,
        Action<FaceIntelligenceRegistrationBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var builder = new FaceIntelligenceRegistrationBuilder(services);
        build(builder);

        if (services.All(d => d.ServiceType != typeof(IFaceEmbedder)))
            throw new InvalidOperationException(
                "No face embedder registered. Inside AddFaceIntelligence(...), call an embedder extension " +
                "such as UseOnnxEmbedder(...) (Shiny.FaceIntelligence.Onnx) or UseEmbedder(...).");

        if (services.All(d => d.ServiceType != typeof(IFaceStore)))
            throw new InvalidOperationException(
                "No face store registered. Inside AddFaceIntelligence(...), call a store extension " +
                "such as UseSqliteStore(...) (Shiny.FaceIntelligence.DocumentDb.Sqlite) or UseStore(...).");

        services.AddSingleton(builder.Options);
        services.AddSingleton<IFaceIntelligence, FaceIntelligenceManager>();
        return services;
    }
}

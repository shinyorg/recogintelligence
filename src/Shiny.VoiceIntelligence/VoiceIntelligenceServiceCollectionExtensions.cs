using Microsoft.Extensions.DependencyInjection;

namespace Shiny.VoiceIntelligence;

public static class VoiceIntelligenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the speaker pipeline. Compose it inside <paramref name="build"/>: pick an embedder
    /// (e.g. <c>UseOnnxEmbedder</c> from Shiny.VoiceIntelligence.Onnx) and a store (e.g. <c>UseSqliteStore</c>
    /// from Shiny.VoiceIntelligence.DocumentDb.Sqlite), and set matching options via <c>builder.Options</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddVoiceIntelligence(voice =>
    /// {
    ///     voice.Options.MaxDistance = 0.7f;
    ///     voice.UseOnnxEmbedder(o => o.ModelBytesProvider = LoadModel);   // ECAPA-TDNN .onnx
    ///     voice.UseSqliteStore(o => o.ConnectionString = "Data Source=voices.db");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddVoiceIntelligence(
        this IServiceCollection services,
        Action<VoiceIntelligenceRegistrationBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var builder = new VoiceIntelligenceRegistrationBuilder(services);
        build(builder);

        if (services.All(d => d.ServiceType != typeof(ISpeakerEmbedder)))
            throw new InvalidOperationException(
                "No speaker embedder registered. Inside AddVoiceIntelligence(...), call an embedder extension " +
                "such as UseOnnxEmbedder(...) (Shiny.VoiceIntelligence.Onnx) or UseEmbedder(...).");

        if (services.All(d => d.ServiceType != typeof(IVoiceStore)))
            throw new InvalidOperationException(
                "No voice store registered. Inside AddVoiceIntelligence(...), call a store extension " +
                "such as UseSqliteStore(...) (Shiny.VoiceIntelligence.DocumentDb.Sqlite) or UseStore(...).");

        services.AddSingleton(builder.Options);
        services.AddSingleton<IVoiceIntelligence, VoiceIntelligenceManager>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace Shiny.VoiceIntelligence;

/// <summary>
/// Composes the speaker pipeline from swappable parts. Embedder and store packages add extension methods
/// here (e.g. <c>UseOnnxEmbedder</c>, <c>UseSqliteStore</c>); the generic <see cref="UseEmbedder(ISpeakerEmbedder)"/>
/// / <see cref="UseStore"/> seams let you plug in anything (a platform-native embedder, a test fake).
/// The voice analogue of <c>FaceIntelligenceRegistrationBuilder</c>.
/// </summary>
public sealed class VoiceIntelligenceRegistrationBuilder
{
    internal VoiceIntelligenceRegistrationBuilder(IServiceCollection services) => this.Services = services;

    /// <summary>The underlying service collection — extension methods register the embedder/store here.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Matching options (distance threshold, candidate count). Mutate directly.</summary>
    public VoiceIntelligenceOptions Options { get; } = new();

    /// <summary>Register a specific embedder instance (e.g. a test fake).</summary>
    public VoiceIntelligenceRegistrationBuilder UseEmbedder(ISpeakerEmbedder embedder)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        return this.UseEmbedder(_ => embedder);
    }

    /// <summary>Register an embedder built from the service provider (lazy — runs on first resolve).</summary>
    public VoiceIntelligenceRegistrationBuilder UseEmbedder(Func<IServiceProvider, ISpeakerEmbedder> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register the voice store built from the service provider.</summary>
    public VoiceIntelligenceRegistrationBuilder UseStore(Func<IServiceProvider, IVoiceStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.Services.AddSingleton(factory);
        return this;
    }
}

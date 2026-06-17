using Microsoft.Extensions.DependencyInjection;

namespace Shiny.FaceIntelligence;

/// <summary>
/// Composes the face pipeline from swappable parts. Embedder and store packages add extension methods
/// here (e.g. <c>UseOnnxEmbedder</c>, <c>UseSqliteStore</c>); the generic <see cref="UseEmbedder(IFaceEmbedder)"/>
/// / <see cref="UseStore"/> seams let you plug in anything (a platform-native embedder, a test fake).
/// </summary>
public sealed class FaceIntelligenceRegistrationBuilder
{
    internal FaceIntelligenceRegistrationBuilder(IServiceCollection services) => this.Services = services;

    /// <summary>The underlying service collection — extension methods register the embedder/store here.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Matching options (distance threshold, candidate count). Mutate directly.</summary>
    public FaceIntelligenceOptions Options { get; } = new();

    /// <summary>Register a specific embedder instance (e.g. a test fake).</summary>
    public FaceIntelligenceRegistrationBuilder UseEmbedder(IFaceEmbedder embedder)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        return this.UseEmbedder(_ => embedder);
    }

    /// <summary>Register an embedder built from the service provider (lazy — runs on first resolve).</summary>
    public FaceIntelligenceRegistrationBuilder UseEmbedder(Func<IServiceProvider, IFaceEmbedder> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.Services.AddSingleton(factory);
        return this;
    }

    /// <summary>Register the face store built from the service provider.</summary>
    public FaceIntelligenceRegistrationBuilder UseStore(Func<IServiceProvider, IFaceStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.Services.AddSingleton(factory);
        return this;
    }
}

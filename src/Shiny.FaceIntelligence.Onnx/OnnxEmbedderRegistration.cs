namespace Shiny.FaceIntelligence.Onnx;

public static class OnnxEmbedderRegistration
{
    /// <summary>
    /// Use the ONNX ArcFace embedder. Configure the model via <see cref="OnnxEmbedderOptions"/>
    /// (lazy <c>ModelBytesProvider</c> preferred for bundled assets). The embedder is built on first
    /// resolve, so a missing model surfaces then (as <see cref="FileNotFoundException"/>) — not at startup.
    /// </summary>
    public static FaceIntelligenceRegistrationBuilder UseOnnxEmbedder(
        this FaceIntelligenceRegistrationBuilder builder,
        Action<OnnxEmbedderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OnnxEmbedderOptions();
        configure(options);

        return builder.UseEmbedder(_ =>
        {
            if (options.ModelBytesProvider is { } provider)
                return new OnnxArcFaceEmbedder(provider());
            if (options.ModelBytes is { } bytes)
                return new OnnxArcFaceEmbedder(bytes);
            if (!string.IsNullOrWhiteSpace(options.ModelPath))
                return new OnnxArcFaceEmbedder(options.ModelPath);

            throw new InvalidOperationException(
                "Configure OnnxEmbedderOptions with ModelBytesProvider, ModelBytes, or ModelPath.");
        });
    }
}

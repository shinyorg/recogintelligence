namespace Shiny.VoiceIntelligence.Onnx;

public static class OnnxEmbedderRegistration
{
    /// <summary>
    /// Use the ONNX ECAPA-TDNN / x-vector speaker embedder. Configure the model via
    /// <see cref="OnnxEmbedderOptions"/> (lazy <c>ModelBytesProvider</c> preferred for bundled assets). The
    /// model loads lazily on the first enroll/recognize, so a missing model surfaces there (as
    /// <see cref="FileNotFoundException"/>, caught by the pages) — never at DI resolve or app startup.
    /// </summary>
    public static VoiceIntelligenceRegistrationBuilder UseOnnxEmbedder(
        this VoiceIntelligenceRegistrationBuilder builder,
        Action<OnnxEmbedderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OnnxEmbedderOptions();
        configure(options);

        return builder.UseEmbedder(_ =>
        {
            // Keep the provider lazy: wrap it in the Func<InferenceSession> ctor so the bytes are only read
            // (and the model only loaded) on the first enroll/recognize, not when the embedder is resolved.
            if (options.ModelBytesProvider is { } provider)
                return new OnnxEcapaEmbedder(() => CreateSession(provider()), options.Dimensions, options.SampleRate);
            if (options.ModelBytes is { } bytes)
                return new OnnxEcapaEmbedder(bytes, options.Dimensions, options.SampleRate);
            if (!string.IsNullOrWhiteSpace(options.ModelPath))
                return new OnnxEcapaEmbedder(options.ModelPath!, options.Dimensions, options.SampleRate);

            throw new InvalidOperationException(
                "Configure OnnxEmbedderOptions with ModelBytesProvider, ModelBytes, or ModelPath.");
        });
    }

    static Microsoft.ML.OnnxRuntime.InferenceSession CreateSession(byte[] modelBytes)
    {
        if (modelBytes is null || modelBytes.Length == 0)
            throw new FileNotFoundException("The speaker ONNX model could not be loaded (the bytes provider returned no data).");
        return new Microsoft.ML.OnnxRuntime.InferenceSession(modelBytes);
    }
}

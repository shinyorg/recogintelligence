using Microsoft.ML.OnnxRuntime;

namespace Shiny.FaceIntelligence.Onnx;

public static class OnnxDetectorRegistration
{
    /// <summary>
    /// Use the ONNX UltraFace detector, enabling the no-box <c>Enroll</c>/<c>Recognize</c> overloads (detect
    /// the face from a raw still, and reject no/low-confidence/multiple/too-small faces). Configure the model
    /// via <see cref="OnnxDetectorOptions"/> (lazy <c>ModelBytesProvider</c> preferred for bundled assets).
    /// The model loads lazily on the first detect, so a missing model surfaces there (as
    /// <see cref="FileNotFoundException"/>, caught by the pages) — never at DI resolve or app startup.
    /// </summary>
    public static FaceIntelligenceRegistrationBuilder UseOnnxDetector(
        this FaceIntelligenceRegistrationBuilder builder,
        Action<OnnxDetectorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OnnxDetectorOptions();
        configure(options);

        return builder.UseDetector(_ =>
        {
            if (options.ModelBytesProvider is { } provider)
                return new OnnxUltraFaceDetector(() => CreateSession(provider()), options);
            if (options.ModelBytes is { } bytes)
                return new OnnxUltraFaceDetector(() => CreateSession(bytes), options);
            if (!string.IsNullOrWhiteSpace(options.ModelPath))
                return new OnnxUltraFaceDetector(() => CreateFromPath(options.ModelPath), options);

            throw new InvalidOperationException(
                "Configure OnnxDetectorOptions with ModelBytesProvider, ModelBytes, or ModelPath.");
        });
    }

    static InferenceSession CreateFromPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException(
                $"Face detector ONNX model not found at '{modelPath}'. Set OnnxDetectorOptions to a UltraFace model.",
                modelPath);
        return new InferenceSession(modelPath);
    }

    static InferenceSession CreateSession(byte[] modelBytes)
    {
        if (modelBytes is null || modelBytes.Length == 0)
            throw new FileNotFoundException("The face detector ONNX model could not be loaded (the bytes provider returned no data).");
        return new InferenceSession(modelBytes);
    }
}

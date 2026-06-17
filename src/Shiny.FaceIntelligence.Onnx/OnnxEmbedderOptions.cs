namespace Shiny.FaceIntelligence.Onnx;

/// <summary>
/// Configures the ONNX ArcFace embedder. Provide the model exactly one way (checked in priority order):
/// <see cref="ModelBytesProvider"/> (lazy — preferred for bundled mobile assets), then
/// <see cref="ModelBytes"/>, then <see cref="ModelPath"/> (file/server).
/// </summary>
public class OnnxEmbedderOptions
{
    /// <summary>Path to a 112×112 ArcFace ONNX model (e.g. <c>w600k_r50.onnx</c>). Use for desktop/server or after copying to app data.</summary>
    public string? ModelPath { get; set; }

    /// <summary>Raw model bytes (already in memory).</summary>
    public byte[]? ModelBytes { get; set; }

    /// <summary>
    /// Lazy model-bytes loader, invoked on first embedder resolve. Preferred on iOS/Android: e.g.
    /// <c>() => ReadAppPackageBytes("arcface.onnx")</c>. Bundled assets aren't real file paths.
    /// </summary>
    public Func<byte[]>? ModelBytesProvider { get; set; }
}

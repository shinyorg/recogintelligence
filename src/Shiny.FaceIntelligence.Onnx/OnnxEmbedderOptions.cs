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
    /// Lazy model-bytes loader, invoked on the <b>first enroll/recognize</b> (not at startup or DI resolve).
    /// Preferred on iOS/Android: e.g. <c>() => ReadAppPackageBytes("arcface.onnx")</c>. Bundled assets aren't
    /// real file paths. A missing model therefore throws <see cref="System.IO.FileNotFoundException"/> from the
    /// first <c>Embed</c> call — where the enroll/recognize pages catch it — instead of crashing at launch.
    /// </summary>
    public Func<byte[]>? ModelBytesProvider { get; set; }

    /// <summary>
    /// Output embedding dimension, reported by the embedder before the model loads so the vector store can
    /// size its index up front (the model loads lazily, so its real dimension isn't known yet). Defaults to
    /// <c>512</c> (ArcFace r50/MobileFaceNet). Set this only if your model emits a different width.
    /// </summary>
    public int Dimensions { get; set; } = 512;
}

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Onnx;

/// <summary>
/// <see cref="IFaceEmbedder"/> backed by an ArcFace-style ONNX model. Preprocessing follows the
/// InsightFace convention: crop → 112×112 RGB → normalize <c>(px - 127.5) / 128</c> → NCHW. The output
/// vector is L2-normalized so cosine distance is directly comparable.
/// </summary>
public sealed class OnnxArcFaceEmbedder : IFaceEmbedder, IDisposable
{
    const int InputSize = 112;

    // The session is created lazily on first Embed, NOT in the constructor. The vector store resolves this
    // embedder up front to read Dimensions; if the constructor loaded the model, a missing/bundled-but-absent
    // model would throw during DI/ViewModel construction (a launch crash) instead of at first enroll/recognize
    // where the pages catch FileNotFoundException. So defer the load and report Dimensions from a hint.
    readonly Lazy<(InferenceSession Session, string InputName)> model;

    /// <summary>
    /// Lazily create the inference session. The session isn't built until the first <see cref="Embed"/>; any
    /// model-load failure (missing file, bad bytes) therefore surfaces from <see cref="Embed"/>, not at
    /// construction. <paramref name="dimensions"/> is the output width reported before the model loads.
    /// </summary>
    public OnnxArcFaceEmbedder(Func<InferenceSession> sessionFactory, int dimensions = 512)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.Dimensions = dimensions > 0 ? dimensions : 512;
        this.model = new Lazy<(InferenceSession, string)>(() =>
        {
            var session = sessionFactory();
            return (session, session.InputMetadata.Keys.First());
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Load the model from a file path (desktop/server, or after copying an asset to app data). Loaded lazily on first use.</summary>
    public OnnxArcFaceEmbedder(string modelPath, int dimensions = 512)
        : this(() => CreateFromPath(modelPath), dimensions) { }

    /// <summary>
    /// Load the model from raw bytes. Preferred on iOS/Android, where a bundled <c>Resources/Raw</c>
    /// asset isn't a real filesystem path: read it with <c>FileSystem.OpenAppPackageFileAsync</c> and
    /// pass the bytes here. Loaded lazily on first use.
    /// </summary>
    public OnnxArcFaceEmbedder(byte[] modelBytes, int dimensions = 512)
        : this(() => CreateFromBytes(modelBytes), dimensions) { }

    static InferenceSession CreateFromPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ArcFace ONNX model not found at '{modelPath}'. Set FaceIntelligenceOptions.ModelPath to a 112×112 ArcFace model (e.g. w600k_r50.onnx).",
                modelPath);
        return new InferenceSession(modelPath);
    }

    static InferenceSession CreateFromBytes(byte[] modelBytes)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        if (modelBytes.Length == 0)
            throw new ArgumentException("ArcFace ONNX model bytes are empty.", nameof(modelBytes));
        return new InferenceSession(modelBytes);
    }

    public int Dimensions { get; }

    public ReadOnlyMemory<float> Embed(ReadOnlySpan<byte> imageData, FaceBox face)
    {
        // Triggers the lazy model load on first call — FileNotFoundException surfaces here, where pages catch it.
        var (session, inputName) = this.model.Value;

        using var bmp = FaceImaging.CropResize(imageData, face, InputSize);
        var input = ToTensor(bmp);

        using var results = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, input)]);

        var output = results.First().AsEnumerable<float>().ToArray();
        L2Normalize(output);
        return output;
    }

    static DenseTensor<float> ToTensor(SKBitmap bmp)
    {
        // NCHW: [1, 3, 112, 112]. Channel-first, normalized to roughly [-1, 1].
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        var pixels = bmp.Pixels; // SKColor[] in row-major order
        for (var y = 0; y < InputSize; y++)
        {
            for (var x = 0; x < InputSize; x++)
            {
                var c = pixels[(y * InputSize) + x];
                tensor[0, 0, y, x] = (c.Red - 127.5f) / 128f;
                tensor[0, 1, y, x] = (c.Green - 127.5f) / 128f;
                tensor[0, 2, y, x] = (c.Blue - 127.5f) / 128f;
            }
        }
        return tensor;
    }

    static void L2Normalize(float[] v)
    {
        var sum = 0f;
        foreach (var f in v)
            sum += f * f;

        var norm = MathF.Sqrt(sum);
        if (norm < 1e-12f)
            return;

        for (var i = 0; i < v.Length; i++)
            v[i] /= norm;
    }

    public void Dispose()
    {
        if (this.model.IsValueCreated)
            this.model.Value.Session.Dispose();
    }
}

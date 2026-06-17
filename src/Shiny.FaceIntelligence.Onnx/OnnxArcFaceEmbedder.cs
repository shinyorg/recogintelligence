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

    readonly InferenceSession session;
    readonly string inputName;

    /// <summary>Load the model from a file path (desktop/server, or after copying an asset to app data).</summary>
    public OnnxArcFaceEmbedder(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException(
                $"ArcFace ONNX model not found at '{modelPath}'. Set FaceIntelligenceOptions.ModelPath to a 112×112 ArcFace model (e.g. w600k_r50.onnx).",
                modelPath);

        this.session = new InferenceSession(modelPath);
        (this.inputName, this.Dimensions) = Introspect(this.session);
    }

    /// <summary>
    /// Load the model from raw bytes. Preferred on iOS/Android, where a bundled <c>Resources/Raw</c>
    /// asset isn't a real filesystem path: read it with <c>FileSystem.OpenAppPackageFileAsync</c> and
    /// pass the bytes here, avoiding the copy-to-app-data dance.
    /// </summary>
    public OnnxArcFaceEmbedder(byte[] modelBytes)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        if (modelBytes.Length == 0)
            throw new ArgumentException("ArcFace ONNX model bytes are empty.", nameof(modelBytes));

        this.session = new InferenceSession(modelBytes);
        (this.inputName, this.Dimensions) = Introspect(this.session);
    }

    static (string InputName, int Dimensions) Introspect(InferenceSession session)
    {
        var inputName = session.InputMetadata.Keys.First();
        var dims = session.OutputMetadata.Values.First().Dimensions.Last();
        if (dims <= 0)
            dims = 512; // dynamic axis — fall back to the ArcFace r50 default
        return (inputName, dims);
    }

    public int Dimensions { get; }

    public ReadOnlyMemory<float> Embed(ReadOnlySpan<byte> imageData, FaceBox face)
    {
        using var bmp = FaceImaging.CropResize(imageData, face, InputSize);
        var input = ToTensor(bmp);

        using var results = this.session.Run(
            [NamedOnnxValue.CreateFromTensor(this.inputName, input)]);

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

    public void Dispose() => this.session.Dispose();
}

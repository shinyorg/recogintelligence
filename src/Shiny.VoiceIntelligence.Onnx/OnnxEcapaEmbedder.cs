using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Shiny.VoiceIntelligence.Onnx;

/// <summary>
/// <see cref="ISpeakerEmbedder"/> backed by an ECAPA-TDNN / x-vector ONNX model. Feeds the raw mono waveform
/// as a <c>[1, samples]</c> float tensor and L2-normalizes the output so cosine distance is directly
/// comparable. The voice analogue of <c>OnnxArcFaceEmbedder</c>.
/// </summary>
/// <remarks>
/// This assumes a <b>raw-waveform</b> model (the common ECAPA export: input <c>[batch, samples]</c> at 16 kHz).
/// A model that expects pre-computed features (fbank/MFCC) needs a feature-extraction step added before
/// <c>session.Run</c> — swap in your own <see cref="ISpeakerEmbedder"/> via <c>UseEmbedder(...)</c> for that.
/// </remarks>
public sealed class OnnxEcapaEmbedder : ISpeakerEmbedder, IDisposable
{
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
    public OnnxEcapaEmbedder(Func<InferenceSession> sessionFactory, int dimensions = 192, int sampleRate = 16000)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.Dimensions = dimensions > 0 ? dimensions : 192;
        this.SampleRate = sampleRate > 0 ? sampleRate : 16000;
        this.model = new Lazy<(InferenceSession, string)>(() =>
        {
            var session = sessionFactory();
            return (session, session.InputMetadata.Keys.First());
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Load the model from a file path (desktop/server, or after copying an asset to app data). Loaded lazily on first use.</summary>
    public OnnxEcapaEmbedder(string modelPath, int dimensions = 192, int sampleRate = 16000)
        : this(() => CreateFromPath(modelPath), dimensions, sampleRate) { }

    /// <summary>
    /// Load the model from raw bytes. Preferred on iOS/Android, where a bundled <c>Resources/Raw</c>
    /// asset isn't a real filesystem path: read it with <c>FileSystem.OpenAppPackageFileAsync</c> and
    /// pass the bytes here. Loaded lazily on first use.
    /// </summary>
    public OnnxEcapaEmbedder(byte[] modelBytes, int dimensions = 192, int sampleRate = 16000)
        : this(() => CreateFromBytes(modelBytes), dimensions, sampleRate) { }

    static InferenceSession CreateFromPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new FileNotFoundException(
                $"Speaker ONNX model not found at '{modelPath}'. Set OnnxEmbedderOptions.ModelPath to an ECAPA-TDNN / x-vector model.",
                modelPath);
        return new InferenceSession(modelPath);
    }

    static InferenceSession CreateFromBytes(byte[] modelBytes)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        if (modelBytes.Length == 0)
            throw new ArgumentException("Speaker ONNX model bytes are empty.", nameof(modelBytes));
        return new InferenceSession(modelBytes);
    }

    public int Dimensions { get; }
    public int SampleRate { get; }

    public ReadOnlyMemory<float> Embed(ReadOnlySpan<float> monoSamples)
    {
        // Triggers the lazy model load on first call — FileNotFoundException surfaces here, where pages catch it.
        var (session, inputName) = this.model.Value;

        // Raw waveform as [1, samples]. Feature-based models need an fbank/MFCC step here instead.
        var input = new DenseTensor<float>(monoSamples.ToArray(), [1, monoSamples.Length]);

        using var results = session.Run(
            [NamedOnnxValue.CreateFromTensor(inputName, input)]);

        var output = results.First().AsEnumerable<float>().ToArray();
        L2Normalize(output);
        return output;
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

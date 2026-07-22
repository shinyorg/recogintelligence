using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Shiny.VoiceIntelligence.Onnx;

/// <summary>
/// <see cref="ISpeakerEmbedder"/> backed by an ECAPA-TDNN / x-vector / CAM++ ONNX model. L2-normalizes the
/// output so cosine distance is directly comparable. The voice analogue of <c>OnnxArcFaceEmbedder</c>.
/// </summary>
/// <remarks>
/// <para>
/// Handles <b>both</b> shapes of speaker model, chosen from the model's own declared input rank on first use:
/// </para>
/// <list type="bullet">
/// <item><b>Raw waveform</b> — input <c>[batch, samples]</c>. The mono samples are fed straight through.</item>
/// <item><b>Fbank features</b> — input <c>[batch, frames, 80]</c> (WeSpeaker / sherpa-onnx exports, whose
/// input is usually named <c>feats</c>). <see cref="KaldiFbank"/> computes the filterbank first.</item>
/// </list>
/// <para>
/// This used to assume raw waveform unconditionally, which made it throw
/// <c>InvalidArgument: Invalid rank for input: feats Got: 2 Expected: 3</c> against any standard WeSpeaker
/// export — i.e. against the model family it is named for. Detection happens when the session loads (first
/// <see cref="Embed"/>), so the lazy-load behaviour is unchanged. Override with
/// <see cref="OnnxEmbedderOptions.InputMode"/> if a model's metadata is misleading.
/// </para>
/// </remarks>
public sealed class OnnxEcapaEmbedder : ISpeakerEmbedder, IDisposable
{
    // The session is created lazily on first Embed, NOT in the constructor. The vector store resolves this
    // embedder up front to read Dimensions; if the constructor loaded the model, a missing/bundled-but-absent
    // model would throw during DI/ViewModel construction (a launch crash) instead of at first enroll/recognize
    // where the pages catch FileNotFoundException. So defer the load and report Dimensions from a hint.
    readonly Lazy<LoadedModel> model;
    readonly OnnxSpeakerInputMode inputMode;

    /// <summary>The resolved session plus how its input must be fed — decided once, when the model loads.</summary>
    sealed record LoadedModel(InferenceSession Session, string InputName, bool FeatureInput);

    /// <summary>
    /// Lazily create the inference session. The session isn't built until the first <see cref="Embed"/>; any
    /// model-load failure (missing file, bad bytes) therefore surfaces from <see cref="Embed"/>, not at
    /// construction. <paramref name="dimensions"/> is the output width reported before the model loads.
    /// </summary>
    public OnnxEcapaEmbedder(
        Func<InferenceSession> sessionFactory,
        int dimensions = 192,
        int sampleRate = 16000,
        OnnxSpeakerInputMode inputMode = OnnxSpeakerInputMode.Auto)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.Dimensions = dimensions > 0 ? dimensions : 192;
        this.SampleRate = sampleRate > 0 ? sampleRate : 16000;
        this.inputMode = inputMode;
        this.model = new Lazy<LoadedModel>(() =>
        {
            var session = sessionFactory();
            var name = session.InputMetadata.Keys.First();
            return new LoadedModel(session, name, this.IsFeatureInput(session, name));
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Decide whether the model wants fbank features or a raw waveform. <see cref="OnnxSpeakerInputMode.Auto"/>
    /// reads the declared input rank: rank 3 means <c>[batch, frames, bins]</c> (features), rank 2 means
    /// <c>[batch, samples]</c> (waveform).
    /// </summary>
    bool IsFeatureInput(InferenceSession session, string inputName)
    {
        if (this.inputMode == OnnxSpeakerInputMode.Waveform)
            return false;
        if (this.inputMode == OnnxSpeakerInputMode.Fbank80)
            return true;

        var dims = session.InputMetadata[inputName].Dimensions;
        if (dims.Length < 3)
            return false;

        // KaldiFbank only produces 80 bins; a model wanting some other width needs its own extractor.
        var bins = dims[^1];
        if (bins > 0 && bins != KaldiFbank.NumBins)
            throw new NotSupportedException(
                $"The speaker model expects {bins}-bin features, but KaldiFbank produces {KaldiFbank.NumBins}. " +
                "Supply a custom ISpeakerEmbedder via UseEmbedder(...) for this model.");

        return true;
    }

    /// <summary>Load the model from a file path (desktop/server, or after copying an asset to app data). Loaded lazily on first use.</summary>
    public OnnxEcapaEmbedder(string modelPath, int dimensions = 192, int sampleRate = 16000, OnnxSpeakerInputMode inputMode = OnnxSpeakerInputMode.Auto)
        : this(() => CreateFromPath(modelPath), dimensions, sampleRate, inputMode) { }

    /// <summary>
    /// Load the model from raw bytes. Preferred on iOS/Android, where a bundled <c>Resources/Raw</c>
    /// asset isn't a real filesystem path: read it with <c>FileSystem.OpenAppPackageFileAsync</c> and
    /// pass the bytes here. Loaded lazily on first use.
    /// </summary>
    public OnnxEcapaEmbedder(byte[] modelBytes, int dimensions = 192, int sampleRate = 16000, OnnxSpeakerInputMode inputMode = OnnxSpeakerInputMode.Auto)
        : this(() => CreateFromBytes(modelBytes), dimensions, sampleRate, inputMode) { }

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
        var loaded = this.model.Value;
        var input = loaded.FeatureInput ? BuildFeatures(monoSamples) : BuildWaveform(monoSamples);

        using var results = loaded.Session.Run(
            [NamedOnnxValue.CreateFromTensor(loaded.InputName, input)]);

        var output = results.First().AsEnumerable<float>().ToArray();

        // A Dimensions hint that disagrees with the model would size the vector store wrong and silently
        // corrupt every stored voiceprint — fail loudly on the first embed instead.
        if (output.Length != this.Dimensions)
            throw new InvalidOperationException(
                $"The speaker model emits {output.Length}-d embeddings but this embedder was configured for " +
                $"{this.Dimensions}. Set OnnxEmbedderOptions.Dimensions = {output.Length} (the vector store is " +
                "sized from it before the model loads).");

        L2Normalize(output);
        return output;
    }

    /// <summary>Raw mono waveform as <c>[1, samples]</c>.</summary>
    static DenseTensor<float> BuildWaveform(ReadOnlySpan<float> monoSamples)
        => new(monoSamples.ToArray(), [1, monoSamples.Length]);

    /// <summary>80-bin kaldi fbank as <c>[1, frames, 80]</c>, the WeSpeaker/sherpa-onnx input format.</summary>
    static DenseTensor<float> BuildFeatures(ReadOnlySpan<float> monoSamples)
    {
        var feats = KaldiFbank.Compute(monoSamples);
        if (feats.Length == 0)
            throw new InvalidOperationException("Audio clip too short to produce any speaker features.");

        var flat = new float[feats.Length * KaldiFbank.NumBins];
        for (var t = 0; t < feats.Length; t++)
            Array.Copy(feats[t], 0, flat, t * KaldiFbank.NumBins, KaldiFbank.NumBins);

        return new DenseTensor<float>(flat, [1, feats.Length, KaldiFbank.NumBins]);
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

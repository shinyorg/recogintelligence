using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Shiny.VoiceIntelligence;

namespace Sample.Features.Voice;

/// <summary>
/// <see cref="ISpeakerEmbedder"/> for a <b>feature-input</b> speaker model — the bundled WeSpeaker CAM++
/// export (<c>ecapa.onnx</c>) consumes 80-bin kaldi fbank features <c>[1, T, 80]</c>, not a raw waveform.
/// So unlike the core <c>OnnxEcapaEmbedder</c> (which feeds <c>[1, samples]</c>), this computes fbank in
/// managed code (<see cref="KaldiFbank"/>) first, then runs the model and L2-normalizes the 512-d output.
///
/// The whole pipeline (fbank config + this model) was validated bit-exact against sherpa-onnx's own
/// extractor. See <see cref="KaldiFbank"/> for the pinned feature parameters.
/// </summary>
public sealed class FbankSpeakerEmbedder : ISpeakerEmbedder, IDisposable
{
    // Lazy, exactly like OnnxEcapaEmbedder: the store resolves this embedder up front only to read
    // Dimensions, so loading the model here (bundled asset absent, bad bytes) would crash at DI/startup
    // instead of at first enroll/recognize where the pages catch FileNotFoundException. Defer the load.
    readonly Lazy<(InferenceSession Session, string InputName)> model;

    public FbankSpeakerEmbedder(Func<byte[]> modelBytesProvider, int dimensions = 512, int sampleRate = 16000)
    {
        ArgumentNullException.ThrowIfNull(modelBytesProvider);
        this.Dimensions = dimensions > 0 ? dimensions : 512;
        this.SampleRate = sampleRate > 0 ? sampleRate : 16000;
        this.model = new Lazy<(InferenceSession, string)>(() =>
        {
            var bytes = modelBytesProvider();
            if (bytes is null || bytes.Length == 0)
                throw new FileNotFoundException("The speaker ONNX model could not be loaded (the bytes provider returned no data).");
            var session = new InferenceSession(bytes);
            return (session, session.InputMetadata.Keys.First());
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int Dimensions { get; }
    public int SampleRate { get; }

    public ReadOnlyMemory<float> Embed(ReadOnlySpan<float> monoSamples)
    {
        // Triggers the lazy model load on first call — FileNotFoundException surfaces here.
        var (session, inputName) = this.model.Value;

        var feats = KaldiFbank.Compute(monoSamples);
        if (feats.Length == 0)
            throw new InvalidOperationException("Audio clip too short to produce any speaker features.");

        var frames = feats.Length;
        var flat = new float[frames * 80];
        for (var t = 0; t < frames; t++)
            Array.Copy(feats[t], 0, flat, t * 80, 80);

        var input = new DenseTensor<float>(flat, [1, frames, 80]);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);

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

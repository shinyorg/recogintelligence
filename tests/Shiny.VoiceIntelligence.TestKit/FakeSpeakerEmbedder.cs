namespace Shiny.VoiceIntelligence.Testing;

/// <summary>
/// Deterministic <see cref="ISpeakerEmbedder"/> for end-to-end tests/benchmarks. Because
/// <see cref="VoiceIntelligenceManager"/> passes the raw sample buffer straight to the embedder (no decode
/// step, unlike the face pipeline), the test simply hands the desired vector in as the "samples" and the fake
/// reads it back — geometry is fully controlled without a real ONNX model. The result is L2-normalized like
/// <c>OnnxEcapaEmbedder</c>, so cosine distance behaves identically.
/// </summary>
public sealed class FakeSpeakerEmbedder(int dimensions, int sampleRate = 16000) : ISpeakerEmbedder
{
    public int Dimensions { get; } = dimensions;
    public int SampleRate { get; } = sampleRate;

    public ReadOnlyMemory<float> Embed(ReadOnlySpan<float> monoSamples)
    {
        // Take the first Dimensions values as the raw vector (zero-padded if the caller passed fewer).
        var v = new float[this.Dimensions];
        var n = Math.Min(this.Dimensions, monoSamples.Length);
        for (var i = 0; i < n; i++)
            v[i] = monoSamples[i];

        var sum = 0f;
        foreach (var f in v)
            sum += f * f;

        var norm = MathF.Sqrt(sum);
        if (norm > 1e-12f)
            for (var i = 0; i < v.Length; i++)
                v[i] /= norm;

        return v;
    }
}

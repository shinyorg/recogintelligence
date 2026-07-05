namespace Shiny.VoiceIntelligence.Testing;

/// <summary>
/// Builds fake "captured utterance" sample buffers for tests/benchmarks. Since
/// <see cref="FakeSpeakerEmbedder"/> reads the sample buffer directly as the target vector, an utterance is
/// just the desired (pre-normalization) voiceprint. Keeps test intent explicit without a real audio model.
/// </summary>
public static class TestVoices
{
    /// <summary>An "utterance" whose <see cref="FakeSpeakerEmbedder"/> embedding is <paramref name="vector"/>.</summary>
    public static float[] Utterance(params float[] vector) => vector;
}

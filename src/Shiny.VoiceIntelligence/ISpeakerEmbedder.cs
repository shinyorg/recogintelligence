namespace Shiny.VoiceIntelligence;

/// <summary>
/// Turns a window of mono audio into a fixed-length, L2-normalized speaker embedding ("voiceprint").
/// The default implementation (<see cref="Onnx.OnnxEcapaEmbedder"/>) runs an ECAPA-TDNN / x-vector ONNX
/// model, but this is the seam for swapping in a platform-native embedder or a test fake.
/// This is the voice analogue of face's <c>IFaceEmbedder</c>.
/// </summary>
public interface ISpeakerEmbedder
{
    /// <summary>The dimensionality of the produced vector (e.g. 192 for ECAPA-TDNN). Must match the vector mapping.</summary>
    int Dimensions { get; }

    /// <summary>
    /// The sample rate, in Hz, that <paramref name="monoSamples"/> passed to <see cref="Embed"/> must be at
    /// (speaker models are trained at a fixed rate — typically 16000). Callers are responsible for resampling.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Embed <paramref name="monoSamples"/> (single-channel PCM normalized to the [-1, 1] range, at
    /// <see cref="SampleRate"/>). Returns an L2-normalized vector of length <see cref="Dimensions"/>.
    /// </summary>
    ReadOnlyMemory<float> Embed(ReadOnlySpan<float> monoSamples);
}

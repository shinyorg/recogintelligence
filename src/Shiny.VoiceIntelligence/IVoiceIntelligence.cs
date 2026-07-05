namespace Shiny.VoiceIntelligence;

/// <summary>
/// The high-level speaker pipeline: enroll named voices and recognize unknown ones. Wraps the
/// <see cref="ISpeakerEmbedder"/> (audio → vector) and the <see cref="IVoiceStore"/> (vector → nearest name).
/// The voice analogue of <c>IFaceIntelligence</c>.
/// </summary>
/// <remarks>
/// Samples are single-channel PCM normalized to [-1, 1] at the embedder's <see cref="ISpeakerEmbedder.SampleRate"/>.
/// Capturing them (microphone, file, stream) is the caller's job — this library never touches audio hardware.
/// </remarks>
public interface IVoiceIntelligence
{
    /// <summary>
    /// Embed <paramref name="samples"/> and store the voiceprint under <paramref name="name"/>. Call several
    /// times per person (ideally the same passphrase, varied a little) to strengthen recognition. Returns the
    /// stored <see cref="Speaker"/> document.
    /// </summary>
    Task<Speaker> Enroll(string name, float[] samples, CancellationToken ct = default);

    /// <summary>
    /// Embed <paramref name="samples"/> and return the nearest enrolled name within the configured distance
    /// threshold, or <see cref="RecognitionResult.NoMatch"/> when nothing is close enough.
    /// </summary>
    Task<RecognitionResult> Recognize(float[] samples, CancellationToken ct = default);

    /// <summary>All enrolled speakers, most-recent first. One entry per stored utterance.</summary>
    Task<IReadOnlyList<Speaker>> GetAll(CancellationToken ct = default);

    /// <summary>Delete every enrolled utterance for a given name. Returns the number removed.</summary>
    Task<int> Forget(string name, CancellationToken ct = default);
}

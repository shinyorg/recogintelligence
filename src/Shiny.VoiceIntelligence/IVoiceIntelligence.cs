namespace Shiny.VoiceIntelligence;

/// <summary>
/// The high-level speaker pipeline: enroll voices against an identity and recognize unknown ones. Wraps the
/// <see cref="ISpeakerEmbedder"/> (audio → vector) and the <see cref="IVoiceStore"/> (vector → nearest identity).
/// Identity is an opaque caller-chosen string — see <see cref="Speaker.PersonIdentifier"/>.
/// The voice analogue of <c>IFaceIntelligence</c>.
/// </summary>
/// <remarks>
/// Samples are single-channel PCM normalized to [-1, 1] at the embedder's <see cref="ISpeakerEmbedder.SampleRate"/>.
/// Capturing them (microphone, file, stream) is the caller's job — this library never touches audio hardware.
/// </remarks>
public interface IVoiceIntelligence
{
    /// <summary>
    /// Embed <paramref name="samples"/> and store the voiceprint under <paramref name="personIdentifier"/>. Call several
    /// times per person (ideally the same passphrase, varied a little) to strengthen recognition. Returns the
    /// stored <see cref="Speaker"/> document.
    /// </summary>
    Task<Speaker> Enroll(string personIdentifier, float[] samples, CancellationToken ct = default);

    /// <summary>
    /// Start a guided enrollment for <paramref name="personIdentifier"/>: prompt-by-prompt recording that checks each
    /// clip and decides for itself when it has enough good ones. Prefer this over calling
    /// <see cref="Enroll"/> in a loop when a person is enrolling themselves — a bad recording that
    /// <see cref="Enroll"/> would happily store becomes a template every future match is compared against.
    /// </summary>
    /// <param name="personIdentifier">Identity to enroll under.</param>
    /// <param name="options">
    /// Prompts and gates. Defaults are derived from <see cref="VoiceIntelligenceOptions.MaxDistance"/>, so
    /// leave this null unless you have a reason.
    /// </param>
    VoiceEnrollmentSession CreateEnrollment(string personIdentifier, VoiceEnrollmentOptions? options = null);

    /// <summary>
    /// Embed <paramref name="samples"/> and return the nearest enrolled identity within the configured distance
    /// threshold, or <see cref="RecognitionResult.NoMatch"/> when nothing is close enough.
    /// </summary>
    Task<RecognitionResult> Recognize(float[] samples, CancellationToken ct = default);

    /// <summary>All enrolled speakers, most-recent first. One entry per stored utterance.</summary>
    Task<IReadOnlyList<Speaker>> GetAll(CancellationToken ct = default);

    /// <summary>Delete every enrolled utterance for a given identity. Returns the number removed.</summary>
    Task<int> Forget(string personIdentifier, CancellationToken ct = default);
}

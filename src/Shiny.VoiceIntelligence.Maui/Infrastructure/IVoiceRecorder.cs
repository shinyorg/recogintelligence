namespace Shiny.VoiceIntelligence.Maui;

/// <summary>
/// How <see cref="VoiceEnrollmentView"/> gets audio. The app implements this; the library does not capture.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that lets a control exist at all without breaking the rule that keeps
/// <see cref="VoiceEnrollmentSession"/> in core: no part of Shiny.VoiceIntelligence opens a microphone. There
/// is no published Shiny audio-capture package to depend on, and baking one platform's capture into a
/// control would make the control useless to an app that already has its own audio pipeline (a VoIP app, one
/// recording from a headset, one enrolling from files).
/// </para>
/// <para>
/// <b>Record enrollment and recognition the same way.</b> The speaker embedding encodes the channel as well
/// as the voice, so a template captured through a different route, sample rate or processing chain than the
/// probe carries a systematic offset that no threshold tuning can remove. Whatever this returns, recognition
/// should be fed by the same code path.
/// </para>
/// </remarks>
public interface IVoiceRecorder
{
    /// <summary>
    /// Record for <paramref name="duration"/> and return the utterance as mono PCM in [-1, 1] at the
    /// embedder's <see cref="ISpeakerEmbedder.SampleRate"/> (16 kHz unless configured otherwise) — the same
    /// format <see cref="IVoiceIntelligence.Enroll"/> takes.
    /// </summary>
    /// <remarks>
    /// Owns its own permission prompt: the control calls this and has no microphone-permission concept of
    /// its own. Throw if permission is refused — the control reports the message.
    /// </remarks>
    Task<float[]> RecordAsync(TimeSpan duration, CancellationToken ct = default);

    /// <summary>
    /// Record, reporting the running input level so the UI can show a VU meter.
    /// </summary>
    /// <param name="level">
    /// Reports <b>linear RMS in [0, 1]</b> over each captured chunk — the raw measure, not a display value.
    /// Scaling it for human eyes (dB) is the control's job, so an implementation doesn't need to know how
    /// it will be drawn. A few updates per second is plenty; report from whatever chunk size the capture
    /// already hands you.
    /// </param>
    /// <remarks>
    /// The default implementation ignores <paramref name="level"/> and forwards to
    /// <see cref="RecordAsync(TimeSpan, CancellationToken)"/>, so a recorder that can't report levels needs
    /// no changes — the meter simply stays idle. Override it when your capture path reads in chunks, which
    /// almost all of them do.
    /// </remarks>
    Task<float[]> RecordAsync(TimeSpan duration, IProgress<float>? level, CancellationToken ct = default)
        => this.RecordAsync(duration, ct);
}

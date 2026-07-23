namespace Shiny.VoiceIntelligence;

/// <summary>Why a recording was not accepted into a <see cref="VoiceEnrollmentSession"/>.</summary>
public enum VoiceEnrollmentRejection
{
    /// <summary>Not a rejection — the recording was accepted.</summary>
    None,

    /// <summary>The buffer is too short to hold a usable utterance.</summary>
    TooShort,

    /// <summary>Long enough, but mostly silence — the person didn't talk for enough of the window.</summary>
    TooLittleSpeech,

    /// <summary>
    /// Speech level too low. Quiet clips are the documented failure mode: their embeddings drift toward a
    /// generic centroid instead of toward the speaker.
    /// </summary>
    TooQuiet,

    /// <summary>Clipped/distorted — too close to the mic, or input gain too high.</summary>
    Clipped,

    /// <summary>Too much background noise relative to the speech.</summary>
    TooNoisy,

    /// <summary>
    /// A clean-sounding recording whose voiceprint is too far from the ones already accepted in this
    /// session. Either the capture went wrong in a way the level checks can't see, or it isn't the same
    /// person.
    /// </summary>
    Inconsistent
}

/// <summary>
/// The outcome of submitting one recording to a <see cref="VoiceEnrollmentSession"/>.
/// </summary>
/// <param name="Accepted">Whether the recording was kept.</param>
/// <param name="Reason"><see cref="VoiceEnrollmentRejection.None"/> when accepted.</param>
/// <param name="Hint">Ready-to-show guidance, e.g. "Speak a little louder, or closer to the mic."</param>
/// <param name="Metrics">What was measured about this recording, accepted or not.</param>
/// <param name="Distance">
/// Cosine distance to the nearest already-accepted recording, or null when this was the first one.
/// </param>
/// <param name="Result">
/// Non-null once the session has finished — at which point the accepted recordings have been stored.
/// </param>
public record VoiceEnrollmentStepResult(
    bool Accepted,
    VoiceEnrollmentRejection Reason,
    string Hint,
    VoiceSampleMetrics Metrics,
    float? Distance,
    VoiceEnrollmentResult? Result
);

/// <summary>
/// A finished enrollment. The stored <see cref="Speakers"/> are already persisted by the time this exists.
/// </summary>
/// <param name="PersonIdentifier">The identity enrolled under.</param>
/// <param name="Speakers">The stored documents — one per accepted recording.</param>
/// <param name="Cohesion">
/// Largest cosine distance between any two stored recordings. The headline quality number: it is how much
/// of the matching budget this person's own templates already consume.
/// </param>
/// <param name="IsConfident">
/// True when <paramref name="Cohesion"/> met <see cref="VoiceEnrollmentOptions.MaxCohesionDistance"/>.
/// False means the session ran out of attempts and stored its best subset anyway — recognition will work,
/// but it's worth re-enrolling somewhere quieter.
/// </param>
public record VoiceEnrollmentResult(
    string PersonIdentifier,
    IReadOnlyList<Speaker> Speakers,
    float Cohesion,
    bool IsConfident
);

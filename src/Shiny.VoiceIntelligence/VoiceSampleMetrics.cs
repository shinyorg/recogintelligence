namespace Shiny.VoiceIntelligence;

/// <summary>
/// What could actually be measured about a captured utterance. Reported on every
/// <see cref="VoiceEnrollmentStepResult"/> so a UI (or a bug report) can say <i>why</i> a recording was
/// turned down rather than just that it was.
/// </summary>
/// <remarks>
/// Levels are in the [-1, 1] sample domain, so they are <b>device- and gain-dependent</b>: with Apple's
/// voice-processing chain disabled (which it must be — see the capture notes) a good iPhone recording sits
/// around 0.01 RMS, not 0.1. Judge levels against what your own capture path produces, not against a number
/// copied from another app.
/// </remarks>
/// <param name="Seconds">Length of the buffer.</param>
/// <param name="Rms">Root-mean-square level over the whole buffer, silence included.</param>
/// <param name="SpeechRms">
/// RMS over the frames judged to be speech. The useful level measure — a long pause before someone starts
/// talking drags <see cref="Rms"/> down without saying anything about how loudly they spoke.
/// </param>
/// <param name="Peak">Largest absolute sample.</param>
/// <param name="ClippedFraction">Fraction of samples at or beyond full scale (distorted).</param>
/// <param name="SpeechSeconds">How much of the buffer was speech rather than silence or noise.</param>
/// <param name="SnrDb">
/// Speech level over noise floor, in dB (90th vs 10th percentile of frame energy). A rough but honest
/// estimate: it cannot separate "quiet room" from "steady hum at speech level".
/// </param>
public readonly record struct VoiceSampleMetrics(
    float Seconds,
    float Rms,
    float SpeechRms,
    float Peak,
    float ClippedFraction,
    float SpeechSeconds,
    float SnrDb
);

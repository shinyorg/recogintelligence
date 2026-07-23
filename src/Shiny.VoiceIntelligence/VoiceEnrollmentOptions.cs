namespace Shiny.VoiceIntelligence;

/// <summary>
/// Tuning for a guided enrollment run (<see cref="VoiceEnrollmentSession"/>): what to prompt, how many good
/// recordings to require, and what counts as a good one.
/// </summary>
/// <remarks>
/// The distance settings are relative to your matching threshold, so build them with
/// <see cref="ForThreshold"/> rather than by hand — <see cref="IVoiceIntelligence.CreateEnrollment"/> does
/// that for you from <see cref="VoiceIntelligenceOptions.MaxDistance"/>.
/// <para>
/// Every audio gate is disabled by setting it to zero (or, for <see cref="MinSnrDb"/>, to a negative
/// number). Server-side callers enrolling from already-clean audio, and tests using a fake embedder that
/// reads the buffer as a vector rather than as sound, want them off.
/// </para>
/// </remarks>
public class VoiceEnrollmentOptions
{
    /// <summary>
    /// Sentences to show, one per recording, cycled in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The model is <b>text-independent</b> — it encodes how someone sounds, not what they said — so these
    /// are not passphrases and nothing checks that the sentence was read (that would need speech-to-text).
    /// They exist for two reasons that do hold: phonetically rich sentences exercise more of the vocal
    /// tract, and having something to read keeps the person talking for the whole window instead of
    /// trailing off after two seconds. These are the standard TIMIT/Harvard prompts.
    /// </para>
    /// <para>
    /// <b>Each prompt is a PAIR of sentences, and that is not cosmetic.</b> One Harvard sentence takes about
    /// 2–2.5 s to read, measured on-device — and <see cref="MinSpeechSeconds"/> asks for 2.5 s of detected
    /// speech. Single-sentence prompts therefore sat right on the gate and were rejected roughly half the
    /// time with "mostly silence", by someone who had in fact read the whole line perfectly (measured: 16
    /// clips, speech duration 2.14–3.62 s, median ~2.45 s, healthy level and 30+ dB SNR throughout). Only the
    /// longest sentence passed reliably, which read as "it accepts the first one and then nothing works".
    /// A pair fills the recording window with continuous speech, which is what the embedding wants anyway.
    /// Being cut off mid-sentence at the end of the window is fine — better than trailing into silence.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Prompts { get; set; } =
    [
        "She had your dark suit in greasy wash water all year. Don't ask me to carry an oily rag like that.",
        "The birch canoe slid on the smooth planks. Glue the sheet to the dark blue background.",
        "It's easy to tell the depth of a well. Four hours of steady work faced us.",
        "The juice of lemons makes fine punch. A box was thrown beside the parked truck.",
        "The hogs were fed chopped corn and garbage. Rice is often served in round bowls.",
        "The boy was there when the sun rose. A rod is used to catch pink salmon."
    ];

    /// <summary>
    /// Fewest accepted recordings before the session can finish. Default 3 — below that there is no
    /// evidence the recordings agree with each other, which is the thing being measured.
    /// </summary>
    public int MinSamples { get; set; } = 3;

    /// <summary>
    /// Most recordings to ask for before giving up on hitting <see cref="MaxCohesionDistance"/> and
    /// finishing with what there is. Default 6.
    /// </summary>
    public int MaxSamples { get; set; } = 6;

    /// <summary>
    /// How far apart the accepted recordings may be from each other (worst pair, cosine distance) for the
    /// enrollment to count as good. Must be meaningfully tighter than the match threshold: a probe gets
    /// compared against these templates, so whatever spread they already have is spread that comes out of
    /// the matching budget. <see cref="ForThreshold"/> sets it to 75% of the threshold.
    /// </summary>
    public float MaxCohesionDistance { get; set; } = 0.30f;

    /// <summary>
    /// A recording further than this from the ones already accepted is rejected rather than stored.
    /// Defaults (via <see cref="ForThreshold"/>) to the match threshold itself: if it would not even
    /// recognize as the same person, it is a bad capture or a different person, and either way it does not
    /// belong in this person's template set.
    /// </summary>
    public float MaxOutlierDistance { get; set; } = 0.40f;

    /// <summary>Minimum speech (not silence) per recording, in seconds. Default 2.5. Zero disables.</summary>
    /// <remarks>
    /// Deliberately <b>not</b> lowered to accommodate short prompts — a speaker embedding needs voiced
    /// material, and dropping this would trade a visible rejection for a quietly weaker template. The prompts
    /// were lengthened instead; see <see cref="Prompts"/>.
    /// </remarks>
    public float MinSpeechSeconds { get; set; } = 2.5f;

    /// <summary>
    /// Minimum <see cref="VoiceSampleMetrics.SpeechRms"/>. Default 0.004, which suits an iPhone built-in mic
    /// with voice processing off; check it against your own capture path. Zero disables.
    /// </summary>
    public float MinSpeechLevel { get; set; } = 0.004f;

    /// <summary>Most clipped (full-scale) samples tolerated, as a fraction. Default 1%. Zero disables.</summary>
    public float MaxClippedFraction { get; set; } = 0.01f;

    /// <summary>Minimum estimated speech-to-noise ratio in dB. Default 10. Negative disables.</summary>
    public float MinSnrDb { get; set; } = 10f;

    /// <summary>
    /// Build options whose distance gates are derived from the matching threshold in use.
    /// </summary>
    /// <param name="maxDistance">The recognizer's <see cref="VoiceIntelligenceOptions.MaxDistance"/>.</param>
    public static VoiceEnrollmentOptions ForThreshold(float maxDistance) => new()
    {
        MaxCohesionDistance = maxDistance * 0.75f,
        MaxOutlierDistance = maxDistance
    };
}

namespace Sample.Features.Voice;

/// <summary>
/// Voice matching thresholds, in one place so the registration and the UI diagnostics can't disagree
/// about what the current threshold actually is.
/// </summary>
public static class VoiceTuning
{
    /// <summary>
    /// Cosine distance below which a voice counts as a match. Lower = stricter.
    /// </summary>
    /// <remarks>
    /// <b>Genuine distances are measured</b>: 8 recordings of one speaker on the built-in iPhone mic,
    /// 28 pairs, gave min 0.011 / p50 0.110 / p95 0.299 / <b>max 0.351</b>. 0.40 accepts all of them
    /// (FRR 0% on the observed set) with a little headroom, and is tighter than any looser value buys.
    /// <para>
    /// <b>FAR is still unmeasured, and cannot be faked.</b> An attempt to synthesise impostors from macOS
    /// TTS voices was invalid: the model rates the legacy MacinTalk synths as the same speaker (Junior vs
    /// Kathy = 0.093), and even the modern voices sit only 0.31–0.43 apart, so synthetic speech does not
    /// occupy the same region of the embedding space as real speech. Getting a real false-accept rate
    /// needs a <b>second human</b> enrolled on this device. The Voice ID page prints the nearest distance
    /// on every attempt, so the data collects itself — have someone else record a few and read the numbers.
    /// </para>
    /// <para>
    /// History, because it misled twice: the original 0.4 came from synthetic audio, then it was raised to
    /// 0.6 on a guess that real voices spread wider. Both were beside the point — matching was failing
    /// because the <i>capture path</i> destroyed the signal (VoiceChat-mode AGC/noise suppression,
    /// Bluetooth routing, and un-filtered decimation from 48 kHz). With that fixed the original 0.4 was
    /// right all along. <b>When recognition fails, suspect the audio before the threshold.</b>
    /// </para>
    /// <para>
    /// Clip quality matters as much as the threshold: 6 of those 8 recordings clustered within 0.11 of
    /// each other, while 2 quieter ones sat 0.20–0.35 out and drifted toward a generic centroid. Enroll
    /// from clips with continuous speech at a consistent distance.
    /// </para>
    /// </remarks>
    public const float MaxDistance = 0.40f;

    /// <summary>
    /// How long both enrollment and identification record for.
    /// </summary>
    /// <remarks>
    /// Deliberately shared. Enroll used to record 5 s and identify 4 s; a pooled speaker model should be
    /// largely length-invariant, but there is no reason to introduce a systematic difference between the
    /// template and the probe when diagnosing matching problems. Longer is better for embedding stability —
    /// 5 s of continuous speech is a reasonable floor.
    /// </remarks>
    public static readonly TimeSpan RecordFor = TimeSpan.FromSeconds(5);
}

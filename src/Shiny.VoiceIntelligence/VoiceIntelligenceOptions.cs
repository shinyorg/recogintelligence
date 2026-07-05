namespace Shiny.VoiceIntelligence;

/// <summary>Matching configuration for the speaker recognizer. Embedder/store config lives in their own packages.</summary>
public class VoiceIntelligenceOptions
{
    /// <summary>
    /// Maximum cosine distance for a match (0 = identical, 2 = opposite). Speaker-embedding models vary a lot,
    /// so this MUST be tuned against your model with real audio and measured false-accept / false-reject rates.
    /// ECAPA-TDNN same-speaker pairs commonly sit below ~0.7 cosine distance (similarity &gt; ~0.3); the default
    /// is deliberately permissive — tighten it for your security tolerance.
    /// </summary>
    public float MaxDistance { get; set; } = 0.7f;

    /// <summary>How many nearest neighbors to pull before applying the threshold. Small is fine.</summary>
    public int CandidateCount { get; set; } = 5;
}

namespace Shiny.FaceIntelligence;

/// <summary>Matching configuration for the face recognizer. Embedder/store config lives in their own packages.</summary>
public class FaceIntelligenceOptions
{
    /// <summary>
    /// Maximum cosine distance for a match (0 = identical, 2 = opposite). ArcFace embeddings of the same
    /// person typically sit below ~0.6 (cosine similarity &gt; 0.4). Tune for your false-accept tolerance.
    /// </summary>
    public float MaxDistance { get; set; } = 0.6f;

    /// <summary>How many nearest neighbors to pull before applying the threshold. Small is fine.</summary>
    public int CandidateCount { get; set; } = 5;
}

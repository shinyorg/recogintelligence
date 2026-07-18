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

    // --- Detection gating (only used by the no-box Enroll/Recognize overloads that run an IFaceDetector) ---

    /// <summary>
    /// Minimum detector confidence (0..1) for a detected face to be accepted. A face below this is treated
    /// as <see cref="FaceDetectionError.LowConfidence"/>. Default 0.6.
    /// </summary>
    public float MinDetectionConfidence { get; set; } = 0.6f;

    /// <summary>
    /// Minimum face size as a fraction of the frame's shorter side (the face box's shorter side ÷ the
    /// image's shorter side). Below this the subject is too far away — <see cref="FaceDetectionError.TooSmall"/>.
    /// Default 0.08 (face ≈ 8% of the frame). Set 0 to disable the size check.
    /// </summary>
    public float MinFaceSizeFraction { get; set; } = 0.08f;

    /// <summary>
    /// When true (default), the no-box <c>Enroll</c> rejects a frame that contains more than one qualifying
    /// face with <see cref="FaceDetectionError.MultipleFaces"/> — it's ambiguous who to enroll.
    /// </summary>
    public bool RejectMultipleFaces { get; set; } = true;

    /// <summary>
    /// When true (default), the no-box <c>Enroll</c> runs a recognition pass before saving; if the face
    /// already matches a <b>different</b> enrolled name within <see cref="MaxDistance"/>, it throws
    /// <see cref="FaceEnrollmentConflictException"/> instead of silently enrolling. Pass
    /// <c>allowDuplicate: true</c> to <c>Enroll</c> to bypass for a single call.
    /// </summary>
    public bool GateEnrollmentOnRecognition { get; set; } = true;
}

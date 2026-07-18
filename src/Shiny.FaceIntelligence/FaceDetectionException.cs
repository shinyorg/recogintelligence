namespace Shiny.FaceIntelligence;

/// <summary>Why a face couldn't be accepted for enrollment/recognition from a raw still.</summary>
public enum FaceDetectionError
{
    /// <summary>No face was found in the frame at all.</summary>
    NoFace,

    /// <summary>A face was found but its detector confidence was below the configured minimum.</summary>
    LowConfidence,

    /// <summary>More than one face was found (ambiguous who to enroll).</summary>
    MultipleFaces,

    /// <summary>The face is too small a fraction of the frame — the subject is too far away.</summary>
    TooSmall
}

/// <summary>
/// Thrown by the no-box <see cref="IFaceIntelligence.Enroll(string, byte[], bool, System.Threading.CancellationToken)"/>
/// (and no-box <c>Recognize</c>) when the detector can't produce a single, good-quality face. The
/// <see cref="Reason"/> tells the UI what to prompt the user to fix.
/// </summary>
public class FaceDetectionException(FaceDetectionError reason, string message) : Exception(message)
{
    /// <summary>The specific reason the face was rejected.</summary>
    public FaceDetectionError Reason { get; } = reason;
}

/// <summary>
/// Thrown by the no-box <c>Enroll</c> when the captured face already matches a <b>different</b> enrolled
/// person within the distance threshold — i.e. you're likely enrolling the wrong face under a new name.
/// Catch it to prompt "looks like {Match.Name} — enroll anyway?" and re-call <c>Enroll</c> with
/// <c>allowDuplicate: true</c> to force it.
/// </summary>
public class FaceEnrollmentConflictException(RecognitionResult match)
    : Exception($"This face already matches '{match.Name}' (distance {match.Distance:0.00}).")
{
    /// <summary>The existing person this face matched.</summary>
    public RecognitionResult Match { get; } = match;
}

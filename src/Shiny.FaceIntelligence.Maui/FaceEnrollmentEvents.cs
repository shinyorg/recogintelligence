namespace Shiny.FaceIntelligence.Maui;

/// <summary>Why a candidate frame was not accepted for the current step. Drives the on-screen hint.</summary>
public enum FaceEnrollmentRejection
{
    /// <summary>No face in frame, or below the detector's confidence floor.</summary>
    NoFace,

    /// <summary>The face is too small — the person needs to move closer for this step.</summary>
    TooFar,

    /// <summary>The face is too large — the person needs to move back for this step.</summary>
    TooClose,

    /// <summary>The face hasn't held still long enough yet.</summary>
    NotSteady,

    /// <summary>Too blurry to make a good template (motion or focus).</summary>
    TooBlurry,

    /// <summary>Too dark to make a good template.</summary>
    TooDark,

    /// <summary>Too bright / blown out.</summary>
    TooBright,

    /// <summary>
    /// The shot is a near-duplicate of one already captured, so it would add nothing to the gallery.
    /// The person needs to actually change pose, not just wait.
    /// </summary>
    TooSimilar
}

/// <summary>Progress through the guided sequence, raised whenever the step or the shot count changes.</summary>
/// <param name="StepIndex">Zero-based index of the current step.</param>
/// <param name="StepCount">Total steps in the sequence.</param>
/// <param name="Instruction">The current step's instruction.</param>
/// <param name="CapturedCount">How many shots have been accepted so far.</param>
public record FaceEnrollmentProgress(int StepIndex, int StepCount, string Instruction, int CapturedCount);

/// <summary>A candidate frame was rejected; <paramref name="Hint"/> is a ready-to-show explanation.</summary>
/// <param name="Reason">Why it was rejected.</param>
/// <param name="Hint">Human-readable guidance, e.g. "Turn a little further — that looked like the last shot".</param>
public record FaceEnrollmentRejected(FaceEnrollmentRejection Reason, string Hint);

/// <summary>
/// The sequence finished. All accepted shots have been enrolled under the person's name by this point.
/// </summary>
/// <param name="PersonIdentifier">The identity they were enrolled under.</param>
/// <param name="People">The stored <see cref="Person"/> documents — one per accepted shot.</param>
/// <param name="MinPairwiseDistance">
/// The smallest cosine distance between any two accepted shots — how tightly clustered the gallery is.
/// A very small value means the shots were more alike than the prompts intended.
/// </param>
/// <param name="SkippedSteps">
/// How many steps timed out without producing a shot. Non-zero is normal — a "move back" step can be
/// unreachable at arm's length — and the shots that were captured are still stored.
/// </param>
public record FaceEnrollmentResult(
    string PersonIdentifier,
    IReadOnlyList<Person> People,
    float MinPairwiseDistance,
    int SkippedSteps = 0);

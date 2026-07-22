namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// One prompt in a guided enrollment sequence: what to ask the person to do, plus the part of it the
/// library can actually <b>check</b>.
/// </summary>
/// <remarks>
/// Be clear about the split. <see cref="Instruction"/> can ask for anything ("turn slightly left"), but
/// <see cref="IFaceDetector"/> reports only a box and a confidence — no landmarks, no yaw/pitch — so head
/// <i>angle</i> cannot be verified. What is verifiable is how much of the frame the face fills
/// (<see cref="MinFaceFraction"/>/<see cref="MaxFaceFraction"/>), image quality, and how different the
/// resulting embedding is from shots already captured. That last check is the one that carries the weight:
/// the point of a varied gallery is embedding spread, and novelty measures it directly rather than trusting
/// that the person actually turned their head.
/// </remarks>
/// <param name="Instruction">Shown to the person, e.g. "Turn your head slightly left".</param>
/// <param name="MinFaceFraction">
/// Smallest the face may be, as a fraction of the frame's shorter side (0 = no minimum). Use for
/// "move closer" steps.
/// </param>
/// <param name="MaxFaceFraction">
/// Largest the face may be, as a fraction of the frame's shorter side (1 = no maximum). Use for
/// "move back" steps.
/// </param>
/// <param name="RequireNovelty">
/// Whether the captured shot must differ from the ones already taken (default <c>true</c>). Set
/// <c>false</c> for a deliberate second look-straight-ahead shot.
/// </param>
public record FaceEnrollmentStep(
    string Instruction,
    float MinFaceFraction = 0f,
    float MaxFaceFraction = 1f,
    bool RequireNovelty = true
)
{
    /// <summary>
    /// The default sequence: front, three angle changes, then near and far. The angle prompts are
    /// unverifiable by design (see the remarks on this type) — the novelty gate is what actually stops six
    /// near-identical front-on shots from being stored.
    /// </summary>
    public static IReadOnlyList<FaceEnrollmentStep> Default { get; } =
    [
        new("Look straight at the camera", MinFaceFraction: 0.20f),
        new("Turn your head slightly left"),
        new("Turn your head slightly right"),
        new("Lift your chin slightly"),
        // The two distance steps are the ones most likely to be unreachable — a phone at arm's length may
        // never get the face under 35% of the frame — so they are deliberately loose, and StepTimeout skips
        // them rather than stalling the sequence.
        new("Move closer to the camera", MinFaceFraction: 0.40f),
        new("Move back a little", MaxFaceFraction: 0.35f)
    ];
}

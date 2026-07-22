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
/// <param name="Guide">
/// The "face hole" to draw and align against. When set, the person must fit their face into it before a
/// shot is taken — which is a real, checkable requirement, unlike the free-text instruction. Moving the
/// target around the frame also varies the angle the camera sees the face from, so it produces genuine pose
/// variation instead of hoping the instruction is followed.
/// </param>
public record FaceEnrollmentStep(
    string Instruction,
    float MinFaceFraction = 0f,
    float MaxFaceFraction = 1f,
    bool RequireNovelty = true,
    FaceGuide? Guide = null
)
{
    /// <summary>
    /// The default sequence: front, three angle changes, then near and far. The angle prompts are
    /// unverifiable by design (see the remarks on this type) — the novelty gate is what actually stops six
    /// near-identical front-on shots from being stored.
    /// </summary>
    public static IReadOnlyList<FaceEnrollmentStep> Default { get; } =
    [
        new("Fit your face in the outline", Guide: new FaceGuide(0.50f, 0.50f, 0.55f)),

        // Off-centre targets: the person physically moves, so the camera sees the face from a different
        // angle. That is pose variation we can actually verify, unlike "turn your head slightly left".
        new("Now move into the outline on the left", Guide: new FaceGuide(0.30f, 0.48f, 0.52f)),
        new("And the outline on the right", Guide: new FaceGuide(0.70f, 0.48f, 0.52f)),
        new("Now the outline near the top", Guide: new FaceGuide(0.50f, 0.32f, 0.50f)),

        // Distance steps. Deliberately loose — a phone at arm's length may never make the face small enough —
        // and StepTimeout skips them rather than stalling the sequence.
        new("Move closer — fill the big outline", Guide: new FaceGuide(0.50f, 0.50f, 0.72f, SizeTolerance: 0.3f)),
        new("Move back — fit the small outline", Guide: new FaceGuide(0.50f, 0.50f, 0.36f, SizeTolerance: 0.35f))
    ];
}

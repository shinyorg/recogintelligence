namespace Shiny.FaceIntelligence.Maui;

/// <summary>One shot a guided enrollment run stored.</summary>
/// <param name="StepIndex">Index into <see cref="FaceEnrollmentView.Steps"/> of the step that produced it.</param>
/// <param name="ShotId">The stored document id (<see cref="Person.Id"/>) — what <c>ForgetShots</c> takes.</param>
/// <param name="ImageData">
/// The upright, mirror-corrected analysis JPEG that was embedded — byte for byte what recognition sees, not
/// the person's display thumbnail. Read-only so a consumer can't alter the record.
/// </param>
/// <param name="ImageWidth">Width of <paramref name="ImageData"/> in pixels.</param>
/// <param name="ImageHeight">Height of <paramref name="ImageData"/> in pixels.</param>
public sealed record FaceEnrollmentCapture(
    int StepIndex,
    string ShotId,
    ReadOnlyMemory<byte> ImageData,
    int ImageWidth,
    int ImageHeight);

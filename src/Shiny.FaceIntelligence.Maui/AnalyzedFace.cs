using Microsoft.Maui.Graphics;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// A face as the analyzer saw it on a live frame: the encoded frame bytes plus the pixel
/// <see cref="FaceBox"/> within them. Handing both to <c>IFaceIntelligence.Enroll</c> stores a
/// template built from exactly the same preprocessing the recognizer applies to every probe.
/// </summary>
/// <param name="ImageData">The analyzed frame, JPEG-encoded, already upright and mirror-corrected.</param>
/// <param name="Box">The face region in <see cref="ImageData"/>'s pixel space.</param>
/// <param name="Bounds">The same region normalized (0..1) — matches <c>OverlayBox</c> geometry.</param>
/// <param name="Confidence">The detector's confidence, 0..1.</param>
/// <param name="StableFrames">How many consecutive frames the face had held steady when this was captured.</param>
/// <param name="ImageWidth">Width of the analyzed (upright, downscaled) frame in pixels.</param>
/// <param name="ImageHeight">Height of the analyzed frame in pixels.</param>
public record AnalyzedFace(
    byte[] ImageData,
    FaceBox Box,
    RectF Bounds,
    float Confidence,
    int StableFrames,
    int ImageWidth = 0,
    int ImageHeight = 0)
{
    /// <summary>
    /// Frame aspect ratio (width / height), needed to map normalized image coordinates into an AspectFill
    /// preview. Falls back to 3:4 when the dimensions weren't supplied.
    /// </summary>
    public float Aspect => this.ImageHeight > 0 ? this.ImageWidth / (float)this.ImageHeight : 0.75f;
}

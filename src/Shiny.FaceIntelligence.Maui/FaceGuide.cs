using Microsoft.Maui.Graphics;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// A target outline ("face hole") drawn over the preview for the person to fit their face into. Geometry is
/// normalized (0..1) in the same upright, mirror-corrected image space as <c>OverlayBox</c>, so it lines up
/// with detections without further correction.
/// </summary>
/// <remarks>
/// This is what makes a guided step <b>checkable</b>. Head angle can't be verified — the detector reports a
/// box, not pose — but "is the face inside this oval, at roughly this size" is plain geometry. It also gets
/// pose variation honestly: moving the face to different positions in the frame changes the angle the camera
/// sees it from, so a left-hand target yields a genuinely different view rather than relying on someone to
/// interpret "turn slightly left" the same way twice.
/// </remarks>
/// <param name="CenterX">Target centre X, 0..1 across the frame.</param>
/// <param name="CenterY">Target centre Y, 0..1 down the frame.</param>
/// <param name="Height">Target face height as a fraction of frame height.</param>
/// <param name="PositionTolerance">
/// How far the face centre may sit from the target centre, in normalized units, and still count as aligned.
/// </param>
/// <param name="SizeTolerance">
/// Allowed relative size error, e.g. 0.35 accepts a face between 65% and 135% of <paramref name="Height"/>.
/// </param>
public record FaceGuide(
    float CenterX = 0.5f,
    float CenterY = 0.5f,
    float Height = 0.55f,
    float PositionTolerance = 0.13f,
    float SizeTolerance = 0.4f
)
{
    /// <summary>
    /// Oval width as a proportion of its height. Applied in <b>view</b> space, not normalized space —
    /// normalized X and Y have different pixel scales unless the frame is square, so deriving a normalized
    /// width from a normalized height would draw a visibly squashed oval on any portrait frame.
    /// </summary>
    public const float AspectRatio = 0.78f;

    /// <summary>
    /// Re-express this guide, whose coordinates are fractions of the <b>visible</b> preview, as fractions of
    /// the full frame — the space detections arrive in.
    /// </summary>
    /// <remarks>
    /// The preview is AspectFill, so the frame is scaled to cover the view and the overflow is cropped: a
    /// 720×1280 frame in a 393×490 view loses ~200px vertically. Authoring guides in full-frame coordinates
    /// therefore puts them partly off-screen and makes them look far bigger than intended, because the
    /// cropped-away region still counts toward "1.0". Visible-space coordinates keep a guide where it was
    /// designed to be on every device aspect.
    /// </remarks>
    /// <param name="imageAspect">Analyzed frame width / height.</param>
    /// <param name="viewAspect">Preview view width / height.</param>
    public FaceGuide ToImageSpace(float imageAspect, float viewAspect)
    {
        if (imageAspect <= 0 || viewAspect <= 0)
            return this;

        // Fraction of each axis that survives the crop, and where the visible window starts.
        var visibleX = imageAspect > viewAspect ? viewAspect / imageAspect : 1f;
        var visibleY = imageAspect < viewAspect ? imageAspect / viewAspect : 1f;

        return this with
        {
            CenterX = ((1f - visibleX) / 2f) + (this.CenterX * visibleX),
            CenterY = ((1f - visibleY) / 2f) + (this.CenterY * visibleY),
            Height = this.Height * visibleY,
            PositionTolerance = this.PositionTolerance * MathF.Min(visibleX, visibleY)
        };
    }

    /// <summary>True when <paramref name="face"/> (normalized image space) sits inside the target at roughly the right size.</summary>
    public bool IsAligned(RectF face)
    {
        var dx = (face.X + (face.Width / 2f)) - this.CenterX;
        var dy = (face.Y + (face.Height / 2f)) - this.CenterY;
        if (MathF.Sqrt((dx * dx) + (dy * dy)) > this.PositionTolerance)
            return false;

        var sizeError = MathF.Abs((face.Height / this.Height) - 1f);
        return sizeError <= this.SizeTolerance;
    }

    /// <summary>Which way the person needs to move to satisfy this guide — drives the on-screen hint.</summary>
    public string? Correction(RectF face)
    {
        var dx = (face.X + (face.Width / 2f)) - this.CenterX;
        var dy = (face.Y + (face.Height / 2f)) - this.CenterY;

        if (MathF.Sqrt((dx * dx) + (dy * dy)) > this.PositionTolerance)
        {
            // Horizontal first — it's the bigger cue when both are off.
            if (MathF.Abs(dx) >= MathF.Abs(dy))
                return dx > 0 ? "Move left into the outline" : "Move right into the outline";
            return dy > 0 ? "Move up into the outline" : "Move down into the outline";
        }

        var ratio = face.Height / this.Height;
        if (ratio < 1f - this.SizeTolerance)
            return "Move closer — fill the outline";
        if (ratio > 1f + this.SizeTolerance)
            return "Move back — you're bigger than the outline";

        return null;
    }
}

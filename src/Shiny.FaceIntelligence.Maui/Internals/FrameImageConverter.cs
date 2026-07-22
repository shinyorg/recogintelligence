using Shiny.Controls.Camera;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// Turns a native <see cref="CameraFrame"/> into an <b>upright, mirror-corrected</b> <see cref="SKBitmap"/> —
/// the one coordinate space everything downstream agrees on. <see cref="OverlayBox"/> geometry is normalized
/// against this same space, so a box drawn from a detection lands where the face appears on the preview with
/// no further correction (this is what the still-capture path kept getting wrong: the camera's normalized
/// bounds and the library's pixel <see cref="FaceBox"/> are different spaces, and the front camera adds a
/// mirror on top).
/// </summary>
/// <remarks>
/// Per-platform because the buffer is: Apple hands over BGRA (a straight copy), Android hands over planar
/// YUV_420_888 (a managed conversion). Both honor <c>maxWidth</c> so the per-frame cost stays bounded —
/// there is no point converting 4K when the detector runs at 320×240.
/// </remarks>
sealed partial class FrameImageConverter
{
    /// <summary>
    /// Convert the frame, downscaling so the result is no wider than <paramref name="maxWidth"/>. Returns
    /// <c>null</c> when the frame isn't the platform type this build understands (the caller then skips the
    /// frame rather than throwing on the analysis thread).
    /// </summary>
    public partial SKBitmap? ToUpright(CameraFrame frame, int maxWidth);

    /// <summary>
    /// Apply the frame's sensor rotation and front-camera mirror so the scene is upright. The mirror is
    /// applied in <i>sensor</i> space (before the rotation) because that's where the flip physically happens —
    /// Skia composes the matrix so the last transform applied is the innermost one.
    /// </summary>
    internal static SKBitmap Orient(SKBitmap src, int rotationDegrees, bool mirrored)
    {
        var rot = ((rotationDegrees % 360) + 360) % 360;
        if (rot == 0 && !mirrored)
            return src;

        var swap = rot is 90 or 270;
        var w = swap ? src.Height : src.Width;
        var h = swap ? src.Width : src.Height;

        var dst = new SKBitmap(w, h, src.ColorType, src.AlphaType);
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Translate(w / 2f, h / 2f);
            canvas.RotateDegrees(rot);
            if (mirrored)
                canvas.Scale(-1, 1);
            canvas.Translate(-src.Width / 2f, -src.Height / 2f);
            canvas.DrawBitmap(src, new SKPoint(0, 0), SKSamplingOptions.Default);
        }
        src.Dispose();
        return dst;
    }

    /// <summary>Downscale to <paramref name="maxWidth"/> when wider; returns the source untouched otherwise.</summary>
    internal static SKBitmap LimitWidth(SKBitmap src, int maxWidth)
    {
        if (maxWidth <= 0 || src.Width <= maxWidth)
            return src;

        var h = (int)Math.Round(src.Height * (maxWidth / (float)src.Width));
        var dst = src.Resize(new SKImageInfo(maxWidth, Math.Max(1, h), src.ColorType, src.AlphaType), SKSamplingOptions.Default);
        src.Dispose();
        return dst ?? src;
    }
}

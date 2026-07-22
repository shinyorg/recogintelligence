using Microsoft.Maui.Graphics;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// Draws the enrollment "face hole": everything outside the target oval is dimmed, the oval itself is
/// outlined, and the outline turns green the moment the detected face fits it.
/// </summary>
/// <remarks>
/// The oval is expressed in normalized <b>image</b> space (as detections are), but has to be painted in
/// <b>view</b> space, and the preview runs AspectFill — the frame is scaled to cover the view and the
/// overflow is cropped. So both the guide and the face box go through <see cref="ToView"/>, which reproduces
/// that same cover-and-centre transform. Skipping it would put the oval somewhere the face can never reach,
/// which is worse than having no guide at all.
/// </remarks>
sealed class FaceGuideDrawable : IDrawable
{
    /// <summary>The target to draw, or null to draw nothing.</summary>
    public FaceGuide? Guide { get; set; }

    /// <summary>Latest detected face, normalized in image space; null when no face is present.</summary>
    public RectF? Face { get; set; }

    /// <summary>Analyzed frame aspect (width / height), for the AspectFill mapping.</summary>
    public float ImageAspect { get; set; } = 0.75f;

    /// <summary>True when the face currently satisfies the guide — flips the outline to green.</summary>
    public bool IsAligned { get; set; }

    /// <summary>Dim applied outside the oval.</summary>
    public Color ScrimColor { get; set; } = Color.FromRgba(0, 0, 0, 0.55f);

    public Color AlignedColor { get; set; } = Colors.LimeGreen;
    public Color UnalignedColor { get; set; } = Color.FromArgb("#FFD27F");

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (this.Guide is not { } guide)
            return;

        var oval = OvalInView(guide, dirtyRect, this.ImageAspect);

        // Dim everything except the oval: clip the view rect minus the ellipse with an even-odd path, then
        // fill. Even-odd is what turns the second (inner) figure into a hole rather than a second solid.
        var path = new PathF();
        path.AppendRectangle(dirtyRect);
        path.AppendEllipse(oval);

        canvas.SaveState();
        canvas.ClipPath(path, WindingMode.EvenOdd);
        canvas.FillColor = this.ScrimColor;
        canvas.FillRectangle(dirtyRect);
        canvas.RestoreState();

        canvas.StrokeColor = this.IsAligned ? this.AlignedColor : this.UnalignedColor;
        canvas.StrokeSize = this.IsAligned ? 5f : 3f;
        canvas.StrokeDashPattern = this.IsAligned ? null : [10f, 6f];
        canvas.DrawEllipse(oval);
        canvas.StrokeDashPattern = null;

        // A faint box on the actual detection makes it obvious which way to move when it's outside the oval.
        if (this.Face is { } face)
        {
            var box = ToView(face, dirtyRect, this.ImageAspect);
            canvas.StrokeColor = this.IsAligned ? this.AlignedColor.WithAlpha(0.5f) : Colors.White.WithAlpha(0.35f);
            canvas.StrokeSize = 1.5f;
            canvas.DrawRectangle(box);
        }
    }

    /// <summary>
    /// The guide oval in view pixels. Height comes from the normalized target; width is derived from that
    /// <i>pixel</i> height so the oval keeps a face-like proportion whatever the frame aspect is.
    /// </summary>
    internal static RectF OvalInView(FaceGuide guide, RectF view, float imageAspect)
    {
        var probe = ToView(new RectF(guide.CenterX, guide.CenterY, 0f, guide.Height), view, imageAspect);
        var h = probe.Height;
        var w = h * FaceGuide.AspectRatio;
        return new RectF(probe.X - (w / 2f), probe.Y - (h / 2f), w, h);
    }

    /// <summary>
    /// Map a normalized image-space rect into view space the way an AspectFill preview does: scale to
    /// <b>cover</b> the view, centre, and let the overflow fall outside.
    /// </summary>
    internal static RectF ToView(RectF normalized, RectF view, float imageAspect)
    {
        if (view.Width <= 0 || view.Height <= 0 || imageAspect <= 0)
            return normalized;

        var viewAspect = view.Width / view.Height;

        // Cover: whichever axis is relatively short gets scaled up and cropped on the other.
        float displayW, displayH;
        if (imageAspect > viewAspect)
        {
            displayH = view.Height;
            displayW = view.Height * imageAspect;
        }
        else
        {
            displayW = view.Width;
            displayH = view.Width / imageAspect;
        }

        var offsetX = view.X + ((view.Width - displayW) / 2f);
        var offsetY = view.Y + ((view.Height - displayH) / 2f);

        return new RectF(
            offsetX + (normalized.X * displayW),
            offsetY + (normalized.Y * displayH),
            normalized.Width * displayW,
            normalized.Height * displayH);
    }
}

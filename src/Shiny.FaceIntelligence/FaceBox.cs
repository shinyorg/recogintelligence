namespace Shiny.FaceIntelligence;

/// <summary>
/// A face region in <b>pixel</b> coordinates of the source image. Note that Shiny's
/// <c>DetectedFace.Bounds</c> are <b>normalized (0..1)</b> in upright image space, so scale them by the
/// captured photo's pixel width/height before constructing a box. Pass the camera photo plus this box to
/// <see cref="IFaceIntelligence.Enroll"/> / <see cref="IFaceIntelligence.Recognize"/>.
/// </summary>
/// <param name="X">Left edge, pixels.</param>
/// <param name="Y">Top edge, pixels.</param>
/// <param name="Width">Width, pixels.</param>
/// <param name="Height">Height, pixels.</param>
public readonly record struct FaceBox(float X, float Y, float Width, float Height)
{
    public float Right => this.X + this.Width;
    public float Bottom => this.Y + this.Height;
}

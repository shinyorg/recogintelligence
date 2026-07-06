using Shiny.FaceIntelligence;
using Shiny.Maui.Controls.Camera.Face;

namespace Sample.Features.Face;

static class FaceDetectionExtensions
{
    /// <summary>
    /// Map a Shiny <see cref="DetectedFace"/> to a Core <see cref="FaceBox"/>. <see cref="DetectedFace.Bounds"/>
    /// is <b>normalized (0..1)</b> in upright image space, but <see cref="FaceBox"/> is in <b>pixels</b>, so
    /// scale by the captured photo's pixel dimensions. (Passing the normalized values straight through crops
    /// a sub-pixel sliver at the top-left — the face is effectively never found.)
    /// </summary>
    public static FaceBox ToFaceBox(this DetectedFace face, int imageWidth, int imageHeight) =>
        new(face.Bounds.X * imageWidth,
            face.Bounds.Y * imageHeight,
            face.Bounds.Width * imageWidth,
            face.Bounds.Height * imageHeight);

    /// <summary>The biggest face in the frame — the subject we want to enroll/recognize.</summary>
    public static DetectedFace? Largest(this IReadOnlyList<DetectedFace> faces)
    {
        DetectedFace? best = null;
        var bestArea = 0f;
        foreach (var f in faces)
        {
            var area = f.Bounds.Width * f.Bounds.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = f;
            }
        }
        return best;
    }
}

using Shiny.FaceIntelligence;
using Shiny.Maui.Controls.Camera.Face;

namespace Sample;

static class FaceDetectionExtensions
{
    /// <summary>Map a Shiny <see cref="DetectedFace"/> (image-space RectF) to a Core <see cref="FaceBox"/>.</summary>
    public static FaceBox ToFaceBox(this DetectedFace face) =>
        new(face.Bounds.X, face.Bounds.Y, face.Bounds.Width, face.Bounds.Height);

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

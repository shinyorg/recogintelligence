using Shiny.FaceIntelligence;
// Alias the camera's detection type — Shiny.FaceIntelligence now also has a DetectedFace (the detector result),
// so the unqualified name is ambiguous. This helper is only for the camera path (the Recognize page).
using CameraFace = Shiny.Maui.Controls.Camera.Face.DetectedFace;

namespace Sample.Features.Face;

static class FaceDetectionExtensions
{
    /// <summary>
    /// Map a Shiny <see cref="CameraFace"/> to a Core <see cref="FaceBox"/>. Its <c>Bounds</c>
    /// is <b>normalized (0..1)</b> in upright image space, but <see cref="FaceBox"/> is in <b>pixels</b>, so
    /// scale by the captured photo's pixel dimensions. (Passing the normalized values straight through crops
    /// a sub-pixel sliver at the top-left — the face is effectively never found.)
    /// </summary>
    public static FaceBox ToFaceBox(this CameraFace face, int imageWidth, int imageHeight) =>
        new(face.Bounds.X * imageWidth,
            face.Bounds.Y * imageHeight,
            face.Bounds.Width * imageWidth,
            face.Bounds.Height * imageHeight);

    /// <summary>The biggest face in the frame — the subject we want to enroll/recognize.</summary>
    public static CameraFace? Largest(this IReadOnlyList<CameraFace> faces)
    {
        CameraFace? best = null;
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

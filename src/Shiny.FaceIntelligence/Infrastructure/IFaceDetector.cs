namespace Shiny.FaceIntelligence;

/// <summary>
/// Finds faces in an image and returns their pixel boxes with a confidence score. This is the seam that
/// lets enrollment/recognition work from a raw still (no externally supplied <see cref="FaceBox"/>) — the
/// default implementation (<c>Onnx.OnnxUltraFaceDetector</c>) runs a lightweight detector ONNX model, but
/// a platform-native detector (iOS Vision, Android ML Kit) can be plugged in the same way.
/// </summary>
/// <remarks>
/// This is a <b>detector</b>, not the <see cref="IFaceEmbedder"/>: it answers "is there a face, where, and
/// how sure am I", which is where enrollment gets a meaningful "no/low-quality/multiple face" error. The
/// embedder never reports quality — it always returns a vector for whatever box it is given.
/// </remarks>
public interface IFaceDetector
{
    /// <summary>
    /// Detect faces in <paramref name="imageData"/> (encoded JPEG/PNG bytes). Returns zero or more
    /// <see cref="DetectedFace"/> with pixel-space boxes, ideally ordered most-confident first. An empty
    /// list means no face was found.
    /// </summary>
    IReadOnlyList<DetectedFace> Detect(ReadOnlySpan<byte> imageData);
}

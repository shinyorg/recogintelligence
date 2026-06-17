namespace Shiny.FaceIntelligence;

/// <summary>
/// Turns a face region of an image into a fixed-length, L2-normalized embedding vector. The default
/// implementation (<see cref="Onnx.OnnxArcFaceEmbedder"/>) runs an ArcFace ONNX model, but this is the
/// seam for swapping in a platform-native embedder (iOS Vision feature print, etc.) later.
/// </summary>
public interface IFaceEmbedder
{
    /// <summary>The dimensionality of the produced vector (e.g. 512 for ArcFace r50). Must match the vector mapping.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embed the face at <paramref name="face"/> within <paramref name="imageData"/> (encoded JPEG/PNG bytes).
    /// Returns an L2-normalized vector of length <see cref="Dimensions"/>.
    /// </summary>
    ReadOnlyMemory<float> Embed(ReadOnlySpan<byte> imageData, FaceBox face);
}

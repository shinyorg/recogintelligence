namespace Shiny.FaceIntelligence.Testing;

/// <summary>
/// Deterministic <see cref="IFaceEmbedder"/> for end-to-end tests/benchmarks. The vector to return is
/// carried by the image bytes (see <see cref="TestFaces"/>), so geometry is fully controlled. The
/// result is L2-normalized like <c>OnnxArcFaceEmbedder</c>, so cosine distance behaves identically.
/// The <see cref="FaceBox"/> is ignored — image preprocessing is out of scope for a fake.
/// </summary>
public sealed class FakeEmbedder(int dimensions) : IFaceEmbedder
{
    public int Dimensions { get; } = dimensions;

    public ReadOnlyMemory<float> Embed(ReadOnlySpan<byte> imageData, FaceBox face)
    {
        var v = TestFaces.Decode(imageData, this.Dimensions);

        var sum = 0f;
        foreach (var f in v)
            sum += f * f;

        var norm = MathF.Sqrt(sum);
        if (norm > 1e-12f)
            for (var i = 0; i < v.Length; i++)
                v[i] /= norm;

        return v;
    }
}

using System.Buffers.Binary;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Testing;

/// <summary>
/// Builds fake "camera photo" byte payloads for tests/benchmarks: a real (decodable) PNG with the
/// desired embedding appended as a trailing block. <see cref="FaceIntelligenceManager"/> decodes the PNG for
/// its thumbnail while <see cref="FakeEmbedder"/> reads the trailing block back as the exact vector,
/// so tests fully control vector geometry without a real ONNX model.
/// </summary>
public static class TestFaces
{
    const uint Magic = 0x464D4245; // 'FEMB' little-endian

    static readonly Lazy<byte[]> SmallPng = new(() =>
    {
        using var bmp = new SKBitmap(8, 8);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    });

    /// <summary>A valid PNG carrying <paramref name="embedding"/> as a trailing payload.</summary>
    public static byte[] Image(params float[] embedding)
    {
        var png = SmallPng.Value;
        var buf = new byte[png.Length + 8 + (embedding.Length * 4)];
        png.CopyTo(buf, 0);

        var p = png.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(p), Magic);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p + 4), embedding.Length);
        for (var i = 0; i < embedding.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(p + 8 + (i * 4)), embedding[i]);

        return buf;
    }

    /// <summary>Reads the trailing embedding written by <see cref="Image"/>.</summary>
    public static float[] Decode(ReadOnlySpan<byte> data, int expectedDim)
    {
        // The payload sits at the very end, so probe the only offset it can occupy.
        var start = data.Length - (8 + (expectedDim * 4));
        if (start >= 0 &&
            BinaryPrimitives.ReadUInt32LittleEndian(data[start..]) == Magic &&
            BinaryPrimitives.ReadInt32LittleEndian(data[(start + 4)..]) == expectedDim)
        {
            var v = new float[expectedDim];
            for (var i = 0; i < expectedDim; i++)
                v[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(start + 8 + (i * 4))..]);
            return v;
        }
        throw new InvalidOperationException(
            $"No {expectedDim}-d FakeEmbedder payload found — build images with TestFaces.Image(...).");
    }
}

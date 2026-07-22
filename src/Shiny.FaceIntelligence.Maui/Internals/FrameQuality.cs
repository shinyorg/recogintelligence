using SkiaSharp;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// Cheap image-quality measures on the face crop, used to keep weak shots out of the gallery.
/// </summary>
/// <remarks>
/// A blurry or badly-exposed enrollment shot still produces a perfectly valid-looking embedding — the
/// embedder never reports "bad input" — it just produces a template that sits away from the person's real
/// cluster and drags recall down. The voice stack showed the same effect: two quiet clips out of eight sat
/// 0.20–0.35 from the rest and pulled toward a generic centroid. Cheaper to reject at capture time.
/// </remarks>
static class FrameQuality
{
    /// <summary>Sharpness (variance of the Laplacian) and mean brightness (0..1) of the face region.</summary>
    public static (float Sharpness, float Brightness) Measure(byte[] imageData, FaceBox box)
    {
        using var bmp = SKBitmap.Decode(imageData);
        if (bmp is null)
            return (0f, 0f);

        // Clamp the box to the bitmap; the detector can return a box that runs off the edge.
        var x0 = Math.Clamp((int)box.X, 0, Math.Max(0, bmp.Width - 1));
        var y0 = Math.Clamp((int)box.Y, 0, Math.Max(0, bmp.Height - 1));
        var x1 = Math.Clamp((int)box.Right, x0 + 1, bmp.Width);
        var y1 = Math.Clamp((int)box.Bottom, y0 + 1, bmp.Height);

        var w = x1 - x0;
        var h = y1 - y0;
        if (w < 3 || h < 3)
            return (0f, 0f);

        // Downsample to a fixed working size: sharpness is scale-sensitive, so measuring at a constant
        // resolution keeps the threshold meaningful whether the face fills the frame or a corner of it.
        const int Size = 96;
        var gray = new float[Size * Size];
        double sum = 0;
        for (var y = 0; y < Size; y++)
        {
            var sy = y0 + (int)((y + 0.5f) * h / Size);
            for (var x = 0; x < Size; x++)
            {
                var sx = x0 + (int)((x + 0.5f) * w / Size);
                var c = bmp.GetPixel(sx, sy);
                var l = (c.Red * 0.299f + c.Green * 0.587f + c.Blue * 0.114f) / 255f;
                gray[y * Size + x] = l;
                sum += l;
            }
        }

        var brightness = (float)(sum / (Size * Size));

        // Variance of the 4-neighbour Laplacian — the standard cheap focus measure.
        double lapSum = 0, lapSumSq = 0;
        var n = 0;
        for (var y = 1; y < Size - 1; y++)
        {
            for (var x = 1; x < Size - 1; x++)
            {
                var i = y * Size + x;
                var lap = (4 * gray[i]) - gray[i - 1] - gray[i + 1] - gray[i - Size] - gray[i + Size];
                lapSum += lap;
                lapSumSq += lap * lap;
                n++;
            }
        }

        if (n == 0)
            return (0f, brightness);

        var mean = lapSum / n;
        var variance = (lapSumSq / n) - (mean * mean);

        // Scaled to a friendlier range so thresholds read as whole numbers rather than 0.0003.
        return ((float)(variance * 1000d), brightness);
    }
}

using SkiaSharp;

namespace Shiny.FaceIntelligence;

/// <summary>SkiaSharp helpers for cropping a face out of a captured frame and resizing/encoding it.</summary>
public static class FaceImaging
{
    /// <summary>
    /// Decode <paramref name="imageData"/>, crop to <paramref name="face"/> (expanded by <paramref name="margin"/>),
    /// and scale to <paramref name="size"/>×<paramref name="size"/>. Returns an RGBA8888 bitmap the caller owns.
    /// </summary>
    public static SKBitmap CropResize(ReadOnlySpan<byte> imageData, FaceBox face, int size, float margin = 0.25f)
    {
        using var source = SKBitmap.Decode(imageData.ToArray())
            ?? throw new InvalidOperationException("Could not decode the captured image.");

        // Expand the box by `margin` on each side, then clamp to the image bounds.
        var mx = face.Width * margin;
        var my = face.Height * margin;
        var left = Math.Max(0, face.X - mx);
        var top = Math.Max(0, face.Y - my);
        var right = Math.Min(source.Width, face.Right + mx);
        var bottom = Math.Min(source.Height, face.Bottom + my);
        var src = new SKRect(left, top, right, bottom);

        var dest = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(dest))
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(source, src, new SKRect(0, 0, size, size), paint);
        }
        return dest;
    }

    /// <summary>Crop the face and encode a small square JPEG thumbnail for display.</summary>
    public static byte[] EncodeThumbnail(ReadOnlySpan<byte> imageData, FaceBox face, int size = 128, int quality = 80)
    {
        using var bmp = CropResize(imageData, face, size);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }
}

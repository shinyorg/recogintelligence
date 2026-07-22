using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Maui;

// Android: CameraX delivers YUV_420_888, which has to be converted in managed code. The loop subsamples
// straight to the target width (rather than converting full-res then resizing) so the per-frame cost scales
// with the analysis size, not the sensor size — the expensive part here is the per-pixel work, not the copy.
partial class FrameImageConverter
{
    public partial SKBitmap? ToUpright(CameraFrame frame, int maxWidth)
    {
        if (frame is not AndroidCameraFrame android)
            return null;

        var planes = android.Proxy.GetPlanes();
        if (planes is null || planes.Length < 3)
            return null;

        int srcW = frame.Width, srcH = frame.Height;
        var step = maxWidth > 0 && srcW > maxWidth ? Math.Max(1, srcW / maxWidth) : 1;
        int w = srcW / step, h = srcH / step;
        if (w <= 0 || h <= 0)
            return null;

        var y = ReadPlane(planes[0]);
        var u = ReadPlane(planes[1]);
        var v = ReadPlane(planes[2]);
        int yRow = planes[0].RowStride, yPix = planes[0].PixelStride;
        int uRow = planes[1].RowStride, uPix = planes[1].PixelStride;
        int vRow = planes[2].RowStride, vPix = planes[2].PixelStride;

        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var dst = new byte[w * h * 4];

        for (var row = 0; row < h; row++)
        {
            var sy = row * step;
            var uvRow = sy / 2;
            for (var col = 0; col < w; col++)
            {
                var sx = col * step;
                var uvCol = sx / 2;

                var yi = sy * yRow + sx * yPix;
                var ui = uvRow * uRow + uvCol * uPix;
                var vi = uvRow * vRow + uvCol * vPix;
                if (yi >= y.Length || ui >= u.Length || vi >= v.Length)
                    continue;

                // BT.601 limited-range YUV -> RGB, integer form.
                var c = y[yi] - 16;
                var d = u[ui] - 128;
                var e = v[vi] - 128;

                var o = (row * w + col) * 4;
                dst[o + 0] = Clamp((298 * c + 516 * d + 128) >> 8);           // B
                dst[o + 1] = Clamp((298 * c - 100 * d - 208 * e + 128) >> 8); // G
                dst[o + 2] = Clamp((298 * c + 409 * e + 128) >> 8);           // R
                dst[o + 3] = 255;
            }
        }

        System.Runtime.InteropServices.Marshal.Copy(dst, 0, bmp.GetPixels(), dst.Length);
        bmp.NotifyPixelsChanged();

        // Already downscaled by `step`, so only orientation is left.
        return Orient(bmp, frame.Rotation, frame.IsMirrored);
    }

    static byte[] ReadPlane(AndroidX.Camera.Core.IImageProxyPlaneProxy plane)
    {
        var buffer = plane.Buffer!;
        buffer.Rewind();
        var data = new byte[buffer.Remaining()];
        buffer.Get(data);
        return data;
    }

    static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}

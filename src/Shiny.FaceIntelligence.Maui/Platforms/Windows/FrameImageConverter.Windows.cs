#if WINDOWS
using System.Runtime.InteropServices;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Maui;

// Windows: WindowsCameraFrame already holds a managed BGRA copy of the SoftwareBitmap, so like Apple this is
// a straight copy into an SKBitmap.
partial class FrameImageConverter
{
    public partial SKBitmap? ToUpright(CameraFrame frame, int maxWidth)
    {
        if (frame is not WindowsCameraFrame windows)
            return null;

        var info = new SKImageInfo(windows.Width, windows.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var bmp = new SKBitmap(info);
        var pixels = bmp.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            bmp.Dispose();
            return null;
        }

        var bgra = windows.Bgra;
        Marshal.Copy(bgra, 0, pixels, Math.Min(bgra.Length, info.BytesSize));
        bmp.NotifyPixelsChanged();

        return LimitWidth(Orient(bmp, frame.Rotation, frame.IsMirrored), maxWidth);
    }
}
#endif

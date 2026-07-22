using System.Runtime.InteropServices;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Maui;

// Apple: AppleCameraFrame already copied the CVPixelBuffer into a managed BGRA array inside the capture
// callback, so this is a memcpy into an SKBitmap — no CoreImage/CGImage round-trip needed.
partial class FrameImageConverter
{
    public partial SKBitmap? ToUpright(CameraFrame frame, int maxWidth)
    {
        if (frame is not AppleCameraFrame apple)
            return null;

        var info = new SKImageInfo(apple.Width, apple.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var bmp = new SKBitmap(info);
        var pixels = bmp.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            bmp.Dispose();
            return null;
        }

        var bgra = apple.Bgra;
        Marshal.Copy(bgra, 0, pixels, Math.Min(bgra.Length, info.BytesSize));
        bmp.NotifyPixelsChanged();

        return LimitWidth(Orient(bmp, frame.Rotation, frame.IsMirrored), maxWidth);
    }
}

using SkiaSharp;

// Native camera entry points are replaced only in the managed bitmap/analyzer tests.
namespace Shiny.Controls.Camera
{
    public sealed class CameraFrame { public SKBitmap? Bitmap { get; init; } }
}
namespace Shiny.FaceIntelligence.Maui
{
    sealed partial class FrameImageConverter
    {
        public partial SKBitmap? ToUpright(Shiny.Controls.Camera.CameraFrame frame, int maxWidth)
            => frame.Bitmap?.Copy();
    }
}

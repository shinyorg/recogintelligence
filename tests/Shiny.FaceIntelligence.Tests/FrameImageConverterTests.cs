using Shiny.FaceIntelligence.Maui;
using SkiaSharp;
using Xunit;

namespace Shiny.FaceIntelligence.Tests
{
    public class FrameImageConverterTests
    {
        [Theory]
        [InlineData(1280, 720, 0, 720, 405)]
        [InlineData(720, 1280, 90, 720, 405)]
        [InlineData(320, 240, 0, 320, 240)]
        public void UprightLimit_RespectsWidthAspectAndNeverUpscales(int w, int h, int rotation, int expectedW, int expectedH)
        {
            var source = new SKBitmap(w, h);
            using var result = FrameImageConverter.LimitWidth(FrameImageConverter.Orient(source, rotation, true), 720);
            Assert.Equal(expectedW, result.Width);
            Assert.Equal(expectedH, result.Height);
        }
    }
}

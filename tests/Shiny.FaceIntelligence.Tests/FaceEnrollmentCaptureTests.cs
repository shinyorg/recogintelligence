using Xunit;

namespace Shiny.FaceIntelligence.Tests;

public class FaceEnrollmentCaptureTests
{
    [Fact]
    public void Snapshot_PreservesOriginalIndexPersonAndImage()
    {
        byte[] bytes = [1, 2, 3];
        var capture = new FaceEnrollmentCapture(4, "stored-id", bytes, 640, 480);
        bytes[0] = 9;
        var result = capture.ImageData;
        result[1] = 9;
        Assert.Equal(4, capture.StepIndex);
        Assert.Equal("stored-id", capture.PersonId);
        Assert.Equal(640, capture.ImageWidth);
        Assert.Equal(480, capture.ImageHeight);
        Assert.Equal(new byte[] { 1, 2, 3 }, capture.ImageData);
    }
}

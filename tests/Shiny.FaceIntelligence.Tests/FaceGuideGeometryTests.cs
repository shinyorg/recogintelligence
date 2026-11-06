using Microsoft.Maui.Graphics;
using Shiny.FaceIntelligence.Maui;
using Xunit;

namespace Shiny.FaceIntelligence.Tests;

public class FaceGuideGeometryTests
{
    [Theory]
    [InlineData(0.5625f)]
    [InlineData(1.7777778f)]
    [InlineData(1f)]
    public void SquarePreview_MatchesConfiguredWidthAndAspectAfterCrop(float imageAspect)
    {
        var guide = new FaceGuide(Height: .55f / .75f) { OvalAspectRatio = .75f };
        var imageGuide = guide.ToImageSpace(imageAspect, 1f);
        var oval = FaceGuideDrawable.OvalInView(imageGuide, new RectF(0, 0, 300, 300), imageAspect);
        Assert.Equal(165f, oval.Width, .01f);
        Assert.Equal(220f, oval.Height, .01f);
        Assert.Equal(150f, oval.Center.X, .01f);
        Assert.Equal(150f, oval.Center.Y, .01f);
    }

    [Fact]
    public void InvalidRatio_FallsBackToDefault()
    {
        var oval = FaceGuideDrawable.OvalInView(new FaceGuide(Height: .5f) { OvalAspectRatio = 0f }, new RectF(0, 0, 300, 300), 1f);
        Assert.Equal(oval.Height * FaceGuide.AspectRatio, oval.Width, .01f);
    }

    [Fact]
    public void ExistingCallers_KeepDefaultRatio()
        => Assert.Equal(.78f, new FaceGuide().OvalAspectRatio);
}

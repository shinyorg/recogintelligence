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
    public void SetPreviewGuide_BeforeFirstFrame_KeepsOvalInPlaceWhenCameraAspectArrives()
    {
        var drawable = new FaceGuideDrawable();
        var guide = new FaceGuide(Height: .8f);
        var preview = new RectF(0, 0, 320, 400);

        drawable.SetPreviewGuide(guide, preview.Width / preview.Height);
        var initial = FaceGuideDrawable.OvalInView(Assert.IsType<FaceGuide>(drawable.Guide), preview, drawable.ImageAspect);
        Assert.Null(drawable.Face);
        Assert.Equal(320f, initial.Height, .01f);
        Assert.Equal(160f, initial.Center.X, .01f);
        Assert.Equal(200f, initial.Center.Y, .01f);

        drawable.ImageAspect = 9f / 16f;
        drawable.SetPreviewGuide(guide, preview.Width / preview.Height);
        var updated = FaceGuideDrawable.OvalInView(Assert.IsType<FaceGuide>(drawable.Guide), preview, drawable.ImageAspect);
        Assert.Equal(initial.X, updated.X, .01f);
        Assert.Equal(initial.Y, updated.Y, .01f);
        Assert.Equal(initial.Width, updated.Width, .01f);
        Assert.Equal(initial.Height, updated.Height, .01f);
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

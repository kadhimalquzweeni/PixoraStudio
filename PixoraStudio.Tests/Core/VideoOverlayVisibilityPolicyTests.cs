using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoOverlayVisibilityPolicyTests
{
    [Fact]
    public void ShouldShow_ShouldReturnTrueWhenOwnerIsActiveAndVideoHostIsVisible()
    {
        Assert.True(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsActive: true,
            ownerIsMinimized: false,
            videoHostIsVisible: true));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void ShouldShow_ShouldReturnFalseWhenOwnerCannotDisplayOverlay(
        bool ownerIsActive,
        bool ownerIsMinimized,
        bool videoHostIsVisible)
    {
        Assert.False(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsActive,
            ownerIsMinimized,
            videoHostIsVisible));
    }
}

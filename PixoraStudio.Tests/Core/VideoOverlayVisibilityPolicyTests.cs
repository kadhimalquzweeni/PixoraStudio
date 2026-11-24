using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoOverlayVisibilityPolicyTests
{
    [Fact]
    public void ShouldShow_ShouldReturnTrueWhenOwnerIsNotMinimizedAndVideoHostIsVisible()
    {
        Assert.True(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsMinimized: false,
            videoHostIsVisible: true));
    }

    [Fact]
    public void ShouldShow_ShouldReturnFalseWhenOwnerIsMinimized()
    {
        Assert.False(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsMinimized: true,
            videoHostIsVisible: true));
    }

    [Fact]
    public void ShouldShow_ShouldReturnFalseWhenVideoHostIsNotVisible()
    {
        Assert.False(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsMinimized: false,
            videoHostIsVisible: false));
    }

    [Fact]
    public void ShouldShow_ShouldReturnFalseWhenOwnerIsMinimizedAndVideoHostIsNotVisible()
    {
        Assert.False(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsMinimized: true,
            videoHostIsVisible: false));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void ShouldKeepControlsVisible_ShouldReturnTrueDuringPointerInteraction(
        bool pointerIsOverOverlay,
        bool overlayHasMouseCapture,
        bool applicationHasMouseCapture)
    {
        Assert.True(VideoOverlayVisibilityPolicy.ShouldKeepControlsVisible(
            pointerIsOverOverlay,
            overlayHasMouseCapture,
            applicationHasMouseCapture));
    }

    [Fact]
    public void ShouldKeepControlsVisible_ShouldReturnFalseWhenPointerIsOutsideAndNothingIsCaptured()
    {
        Assert.False(VideoOverlayVisibilityPolicy.ShouldKeepControlsVisible(
            pointerIsOverOverlay: false,
            overlayHasMouseCapture: false,
            applicationHasMouseCapture: false));
    }
}

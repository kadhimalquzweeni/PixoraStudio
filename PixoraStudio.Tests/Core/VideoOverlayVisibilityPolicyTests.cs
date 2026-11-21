using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VideoOverlayVisibilityPolicyTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldShow_ShouldReturnTrueWhenEitherApplicationWindowIsActive(
        bool ownerIsActive,
        bool overlayIsActive)
    {
        Assert.True(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsActive,
            overlayIsActive,
            ownerIsMinimized: false,
            videoHostIsVisible: true));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    public void ShouldShow_ShouldReturnFalseWhenBothWindowsAreInactiveOrOwnerCannotDisplay(
        bool ownerIsActive,
        bool overlayIsActive,
        bool ownerIsMinimized,
        bool videoHostIsVisible)
    {
        Assert.False(VideoOverlayVisibilityPolicy.ShouldShow(
            ownerIsActive,
            overlayIsActive,
            ownerIsMinimized,
            videoHostIsVisible));
    }
}

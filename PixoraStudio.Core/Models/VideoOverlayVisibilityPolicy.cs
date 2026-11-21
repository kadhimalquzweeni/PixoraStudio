namespace PixoraStudio.Core.Models;

/// <summary>
/// Determines whether the video preview overlay may be displayed.
/// </summary>
public static class VideoOverlayVisibilityPolicy
{
    public static bool ShouldShow(
        bool ownerIsActive,
        bool ownerIsMinimized,
        bool videoHostIsVisible)
    {
        return ownerIsActive &&
               !ownerIsMinimized &&
               videoHostIsVisible;
    }
}

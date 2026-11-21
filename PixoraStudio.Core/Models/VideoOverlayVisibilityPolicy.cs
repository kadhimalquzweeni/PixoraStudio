namespace PixoraStudio.Core.Models;

/// <summary>
/// Determines whether the video preview overlay may be displayed.
/// </summary>
public static class VideoOverlayVisibilityPolicy
{
    public static bool ShouldShow(
        bool ownerIsActive,
        bool overlayIsActive,
        bool ownerIsMinimized,
        bool videoHostIsVisible)
    {
        // Clicking a control can activate the owned overlay window and
        // temporarily deactivate its owner. Both windows belong to the
        // same application, so either being active keeps the overlay alive.
        return (ownerIsActive || overlayIsActive) &&
               !ownerIsMinimized &&
               videoHostIsVisible;
    }
    
    /// <summary>
    /// Determines whether hover controls must remain visible during pointer interaction.
    /// </summary>
    public static bool ShouldKeepControlsVisible(
        bool pointerIsOverOverlay,
        bool overlayHasMouseCapture,
        bool applicationHasMouseCapture)
    {
        return pointerIsOverOverlay ||
               overlayHasMouseCapture ||
               applicationHasMouseCapture;
    }
}

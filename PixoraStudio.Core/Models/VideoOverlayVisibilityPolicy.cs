namespace PixoraStudio.Core.Models;

/// <summary>
/// Determines whether the video preview overlay may be displayed.
/// </summary>
public static class VideoOverlayVisibilityPolicy
{
    public static bool ShouldShow(
        bool ownerIsMinimized,
        bool videoHostIsVisible)
    {
        // Do not tie visibility to window activation. Clicking an owned
        // overlay can deactivate the editor window, but must not hide the
        // overlay before the control receives its click.
        return !ownerIsMinimized && videoHostIsVisible;
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

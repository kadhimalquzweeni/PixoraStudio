namespace PixoraStudio.Core.Models;

/// <summary>
/// Chooses how precisely a thumbnail must match its requested position.
/// </summary>
public enum VideoThumbnailMode
{
    /// <summary>
    /// The thumbnail shows the frame at the requested position. The decoder
    /// starts at the preceding keyframe and decodes forward to that frame,
    /// which can cost a whole GOP of decoding per keyframe interval.
    /// </summary>
    Exact,

    /// <summary>
    /// The thumbnail shows the keyframe at or before the requested position.
    /// Only that one picture is decoded, so each distinct keyframe costs a
    /// single frame regardless of GOP length. Requested positions that fall
    /// between the same pair of keyframes share one image. This is the usual
    /// fast keyframe seek and suits filmstrips and scrub previews.
    /// </summary>
    NearestKeyframe
}

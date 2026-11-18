namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Picture order values derived from an H.264 slice header.</summary>
public sealed record H264PictureOrderCount(
    int TopFieldOrderCount,
    int BottomFieldOrderCount,
    int PictureOrderCount,
    int FrameNumOffset,
    bool HasTopField,
    bool HasBottomField);

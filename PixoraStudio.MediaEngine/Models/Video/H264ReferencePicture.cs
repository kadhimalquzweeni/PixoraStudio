namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Identifies a frame-coded reference picture for list construction.
/// FrameNum is the picture's frame_num; LongTermFrameIndex is used only
/// when IsLongTerm is true. PictureOrderCount is retained for output ordering.
/// </summary>
public sealed record H264ReferencePicture(
    int FrameNum,
    int PictureOrderCount,
    bool IsLongTerm = false,
    int LongTermFrameIndex = 0);

using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Frame-coded B-slice reference lists after list modification.</summary>
public sealed record H264BReferencePictureLists(
    IReadOnlyList<H264ReferencePicture> List0,
    IReadOnlyList<H264ReferencePicture> List1);

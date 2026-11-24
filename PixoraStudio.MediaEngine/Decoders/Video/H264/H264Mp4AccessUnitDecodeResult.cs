using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Result of decoding one MP4 AVC sample as a complete, single-slice picture.
/// ReadyFrames may contain earlier pictures released by presentation reordering.
/// </summary>
public sealed record H264Mp4AccessUnitDecodeResult(
    H264Mp4VideoSample SourceSample,
    H264SliceHeader SliceHeader,
    H264PictureOrderCount PictureOrderCount,
    H264DecodedPictureSurface Surface,
    IReadOnlyList<H264Mp4DecodedVideoFrame> ReadyFrames);

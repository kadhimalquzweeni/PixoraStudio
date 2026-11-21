namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// A decoded frame that satisfies one requested presentation-time target.
/// <see cref="TargetIndex"/> is the index into the target list passed to
/// <see cref="H264Mp4TrackDecoder.DecodeTargetsFromSample"/>. One frame can
/// satisfy several targets when they fall within the same frame interval.
/// </summary>
public readonly record struct H264Mp4TargetFrame(
    int TargetIndex,
    H264Mp4DecodedVideoFrame Frame);

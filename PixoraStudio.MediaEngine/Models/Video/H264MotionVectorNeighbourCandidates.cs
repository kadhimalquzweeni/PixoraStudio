namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// The four spatial neighbours used by H.264 list-0 motion-vector prediction.
/// Coordinates are resolved by the picture state; null means unavailable.
/// An intra-coded neighbour is represented by ReferenceIndex == -1, distinct
/// from an unavailable neighbour so C-to-D fallback is applied correctly.
/// </summary>
public readonly record struct H264MotionVectorNeighbourCandidates(
    H264MotionVectorCandidate? Left,
    H264MotionVectorCandidate? Above,
    H264MotionVectorCandidate? AboveRight,
    H264MotionVectorCandidate? AboveLeft);

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// One inter-prediction sub-partition after list-0 motion-vector prediction
/// and MVD addition. Coordinates and dimensions are in 4x4-luma-block units.
/// </summary>
public sealed record H264ReconstructedInterPartition(
    int MacroblockPartitionIndex,
    int SubPartitionIndex,
    int X,
    int Y,
    int Width,
    int Height,
    int ReferenceIndex,
    H264MotionVector MotionVector);

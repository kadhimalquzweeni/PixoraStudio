namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Reconstructed motion information for one explicit B-slice sub-partition.
/// A null list vector/reference means that prediction direction is not used.
/// </summary>
public sealed record H264ReconstructedBInterSubPartition(
    int MacroblockPartitionIndex,
    int SubPartitionIndex,
    int X,
    int Y,
    int Width,
    int Height,
    int? ReferenceIndexL0,
    int? ReferenceIndexL1,
    H264MotionVector? MotionVectorL0,
    H264MotionVector? MotionVectorL1);

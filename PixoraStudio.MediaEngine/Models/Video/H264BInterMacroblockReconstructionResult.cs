using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Result of reconstructing and committing one coded B inter or B_Skip macroblock.</summary>
public sealed record H264BInterMacroblockReconstructionResult(
    uint MacroblockAddress,
    int QpY,
    H264CodedBlockPattern CodedBlockPattern,
    bool IsSkipped,
    IReadOnlyList<H264ReconstructedBInterSubPartition> Partitions,
    H264MotionCompensatedBlock Prediction,
    H264MotionCompensatedBlock Reconstruction);

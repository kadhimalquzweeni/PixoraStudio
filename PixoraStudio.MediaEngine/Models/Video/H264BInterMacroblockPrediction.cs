using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>One reconstructed B-slice sub-partition and its final prediction samples.</summary>
public sealed record H264BInterMacroblockPrediction(
    H264ReconstructedBInterSubPartition Partition,
    H264MotionCompensatedBlock Prediction);

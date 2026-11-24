namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Motion-compensated prediction associated with one reconstructed list-0
/// inter partition. Residual decoding and final sample addition are separate.
/// </summary>
public sealed record H264InterMacroblockPrediction(
    H264ReconstructedInterPartition Partition,
    H264MotionCompensatedBlock Prediction);

using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Result of fully decoding one non-skipped P inter macroblock, including
/// its assembled prediction, decoded residual and committed reconstruction.
/// </summary>
public sealed record H264InterMacroblockReconstructionResult(
    uint MacroblockAddress,
    int QpY,
    H264CodedBlockPattern CodedBlockPattern,
    IReadOnlyList<H264InterMacroblockPrediction> Partitions,
    H264MotionCompensatedBlock Prediction,
    H264MotionCompensatedBlock Reconstructed);

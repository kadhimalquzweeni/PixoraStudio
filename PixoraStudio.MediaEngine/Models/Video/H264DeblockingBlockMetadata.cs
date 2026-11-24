using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Prediction and residual state for one 4x4 luma block, used when deriving
/// the H.264 deblocking boundary strength.
/// </summary>
public sealed record H264DeblockingBlockMetadata(
    bool IsIntra,
    bool HasNonZeroCoefficients,
    H264ReferencePicture? ReferencePictureL0,
    H264MotionVector? MotionVectorL0,
    H264ReferencePicture? ReferencePictureL1,
    H264MotionVector? MotionVectorL1);

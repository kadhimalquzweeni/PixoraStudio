namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>A motion vector in quarter-luma-sample units.</summary>
public readonly record struct H264MotionVector(int Horizontal, int Vertical);

/// <summary>
/// Motion-vector and reference-index information for one neighbouring partition.
/// A null candidate supplied to the predictor represents an unavailable neighbour.
/// </summary>
public readonly record struct H264MotionVectorCandidate(
    int ReferenceIndex,
    H264MotionVector MotionVector);

/// <summary>Shape of the current macroblock partition for H.264 P-slice MVP rules.</summary>
public enum H264MotionVectorPartitionShape
{
    SixteenBySixteen,
    SixteenByEight,
    EightBySixteen,
    SubMacroblock
}

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Temporal direct prediction inputs for one collocated 4x4 region.
/// All picture-order counts are in the same POC domain and refer to frame pictures.
/// </summary>
public sealed record H264TemporalDirectPredictionContext(
    H264MotionVector CollocatedMotionVector,
    int CurrentPictureOrderCount,
    int CollocatedPictureOrderCount,
    int CollocatedReferencePictureOrderCount,
    bool CollocatedReferenceIsLongTerm,
    int CollocatedReferenceIndex = 0,
    bool CollocatedPictureIsShortTerm = false,
    int MappedReferenceIndexL0 = 0);

/// <summary>The two derived motion vectors and the temporal distance scale factor.</summary>
public sealed record H264TemporalDirectMotionVectorResult(
    H264MotionVector MotionVectorL0,
    H264MotionVector MotionVectorL1,
    int DistanceScaleFactor,
    int ReferenceIndexL0 = 0);

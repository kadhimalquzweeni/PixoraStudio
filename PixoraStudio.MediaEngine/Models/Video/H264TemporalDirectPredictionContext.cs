namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Temporal/spatial direct metadata for one collocated 4x4 region. The
/// collocated picture is RefPicList1[0]; its referenced picture is pic0.
/// All POCs are in the same frame-picture POC domain.
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

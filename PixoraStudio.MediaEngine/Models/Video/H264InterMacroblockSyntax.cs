namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>A decoded signed horizontal/vertical motion-vector difference in quarter-luma-sample units.</summary>
public sealed record H264MotionVectorDifference(int Horizontal, int Vertical);

/// <summary>One rectangular sub-partition inside an inter-prediction partition.</summary>
public sealed record H264InterSubPartition(
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<H264MotionVectorDifference> MotionVectorDifferences);

/// <summary>A P-slice list-0 prediction partition and its reference index.</summary>
public sealed record H264InterPredictionPartition(
    int X,
    int Y,
    int Width,
    int Height,
    uint ReferenceIndex,
    IReadOnlyList<H264InterSubPartition> SubPartitions);

/// <summary>
/// Parsed CAVLC P-macroblock inter-prediction syntax. Motion-vector differences
/// are retained in syntax order; prediction and reconstruction are later stages.
/// </summary>
public sealed record H264InterMacroblockSyntax(
    uint MacroblockType,
    bool UsesSubMacroblockPartitions,
    bool ReferenceIndexForcedToZero,
    IReadOnlyList<H264InterPredictionPartition> Partitions);

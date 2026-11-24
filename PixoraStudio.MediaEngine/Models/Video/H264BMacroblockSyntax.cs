namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Prediction direction(s) used by one B-slice partition.</summary>
[Flags]
public enum H264BPredictionDirection
{
    Direct = 0,
    List0 = 1,
    List1 = 2,
    Bi = List0 | List1
}

/// <summary>A rectangular B-slice sub-partition with list-specific MVD values.</summary>
public sealed record H264BSubPartition(
    int X,
    int Y,
    int Width,
    int Height,
    H264BPredictionDirection Direction,
    H264MotionVectorDifference? MotionVectorDifferenceL0,
    H264MotionVectorDifference? MotionVectorDifferenceL1);

/// <summary>One B-slice macroblock partition and its optional reference indices.</summary>
public sealed record H264BPredictionPartition(
    int X,
    int Y,
    int Width,
    int Height,
    H264BPredictionDirection Direction,
    uint? ReferenceIndexL0,
    uint? ReferenceIndexL1,
    IReadOnlyList<H264BSubPartition> SubPartitions);

/// <summary>Parsed frame-coded B-slice inter prediction syntax.</summary>
public sealed record H264BMacroblockSyntax(
    uint MacroblockType,
    bool UsesSubMacroblockPartitions,
    IReadOnlyList<H264BPredictionPartition> Partitions);

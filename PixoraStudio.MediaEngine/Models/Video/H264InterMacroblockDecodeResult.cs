namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Result of parsing one P-slice inter macroblock and reconstructing its
/// list-0 motion vectors. Pixel prediction and residual reconstruction are
/// intentionally separate future stages.
/// </summary>
public sealed record H264InterMacroblockDecodeResult(
    H264InterMacroblockSyntax Syntax,
    IReadOnlyList<H264ReconstructedInterPartition> ReconstructedPartitions);

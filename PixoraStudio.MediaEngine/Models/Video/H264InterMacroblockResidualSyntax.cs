using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Parsed coded-block-pattern and optional QP delta for an inter macroblock.
/// </summary>
public sealed record H264InterMacroblockResidualSyntax(
    H264CodedBlockPattern CodedBlockPattern,
    int? MacroblockQpDelta);

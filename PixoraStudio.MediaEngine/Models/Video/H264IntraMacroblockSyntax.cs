namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Parsed prediction and quantisation syntax following an intra macroblock
/// type prefix. Neighbour-dependent prediction and residual coefficients are
/// decoded by later stages.
/// </summary>
public sealed record H264IntraMacroblockSyntax(
    bool? TransformSize8X8Flag,
    H264IntraPredictionModeSyntax? LumaPredictionModes,
    uint? IntraChromaPredictionMode,
    H264CodedBlockPattern CodedBlockPattern,
    int? MacroblockQpDelta);

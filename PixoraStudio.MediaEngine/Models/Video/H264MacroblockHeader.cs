namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Parsed macroblock type prefix. Inter prediction and residual payload syntax
/// are deliberately left to later decoding stages.
/// </summary>
public sealed record H264MacroblockHeader(
    uint MacroblockAddress,
    uint MbTypeCode,
    bool IsSkipped,
    uint SkippedMacroblockCount,
    bool IsIntra,
    bool IsIntra16x16,
    uint? Intra16x16PredMode,
    uint? CodedBlockPatternLuma,
    uint? CodedBlockPatternChroma,
    bool IsPcm);

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Parsed intra-macroblock coded block pattern. The codeNum is retained for
/// diagnostics while the luma and chroma patterns are exposed separately.
/// </summary>
public sealed record H264CodedBlockPattern(
    uint CodeNum,
    uint Luma,
    uint Chroma,
    uint ChromaArrayType);

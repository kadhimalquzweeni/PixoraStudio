using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// CAVLC residual payload for one inter-coded P-slice macroblock. Luma and
/// chroma blocks retain their forward scan-order coefficient levels.
/// </summary>
public sealed record H264InterResidualMacroblock(
    uint MacroblockAddress,
    IReadOnlyList<H264IntraLumaResidualBlock> LumaBlocks,
    IReadOnlyList<H264IntraChromaResidualComponent> ChromaComponents)
{
    public bool TransformSize8X8Flag { get; init; }
    public IReadOnlyList<H264Intra8x8LumaResidualBlock> Luma8x8Blocks { get; init; } = [];
}

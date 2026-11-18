namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Decoded CAVLC luma residual payload for one intra macroblock.
/// Coefficients in each block remain in forward scan order.
/// </summary>
public sealed record H264IntraLumaResidualMacroblock(
    uint MacroblockAddress,
    H264CavlcResidualBlock? Intra16x16Dc,
    IReadOnlyList<H264IntraLumaResidualBlock> Blocks)
{
    public IReadOnlyList<H264IntraChromaResidualComponent> ChromaComponents { get; init; } = [];
    public IReadOnlyList<H264Intra8x8LumaResidualBlock> Intra8x8Blocks { get; init; } = [];
}

/// <summary>
/// One luma 4x4 residual block, identified by its local 4x4 coordinates
/// within the macroblock.
/// </summary>
public sealed record H264IntraLumaResidualBlock(
    int BlockX,
    int BlockY,
    bool IsCoded,
    H264CavlcResidualBlock Residual);

 
/// <summary>Decoded Cb or Cr residual payload for one intra macroblock.</summary>
public sealed record H264IntraChromaResidualComponent(
    byte ComponentId,
    H264CavlcResidualBlock? Dc,
    IReadOnlyList<H264IntraChromaResidualBlock> Blocks);

/// <summary>One 4x4 chroma AC residual block in macroblock-local coordinates.</summary>
public sealed record H264IntraChromaResidualBlock(
    int BlockX,
    int BlockY,
    bool IsCoded,
    H264CavlcResidualBlock Residual);

/// <summary>One CAVLC-decoded 8x8 luma transform block in 8x8 scan order.</summary>
public sealed record H264Intra8x8LumaResidualBlock(
    int BlockX,
    int BlockY,
    bool IsCoded,
    IReadOnlyList<int> CoefficientsInScanOrder);

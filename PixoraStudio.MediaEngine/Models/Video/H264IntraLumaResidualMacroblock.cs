namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Decoded CAVLC luma residual payload for one intra macroblock.
/// Coefficients in each block remain in forward scan order.
/// </summary>
public sealed record H264IntraLumaResidualMacroblock(
    uint MacroblockAddress,
    H264CavlcResidualBlock? Intra16x16Dc,
    IReadOnlyList<H264IntraLumaResidualBlock> Blocks);

/// <summary>
/// One luma 4x4 residual block, identified by its local 4x4 coordinates
/// within the macroblock.
/// </summary>
public sealed record H264IntraLumaResidualBlock(
    int BlockX,
    int BlockY,
    bool IsCoded,
    H264CavlcResidualBlock Residual);

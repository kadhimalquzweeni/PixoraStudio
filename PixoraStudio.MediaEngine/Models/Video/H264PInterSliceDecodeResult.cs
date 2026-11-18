using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Summary of a decoded P slice. Each result represents a fully reconstructed
/// inter macroblock committed to the destination picture surface.
/// </summary>
public sealed record H264PInterSliceDecodeResult(
    H264SliceHeader SliceHeader,
    int MacroblocksDecoded,
    int FinalQpY,
    int BitsConsumed,
    IReadOnlyList<H264InterMacroblockReconstructionResult> Macroblocks)
{
    public IReadOnlyList<uint> IntraMacroblockAddresses { get; init; } = [];
}

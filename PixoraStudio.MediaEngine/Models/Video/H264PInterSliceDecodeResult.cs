using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Summary of a decoded P slice. Inter macroblock results and intra macroblock
/// addresses are reported separately; MacroblocksDecoded counts both.
/// </summary>
public sealed record H264PInterSliceDecodeResult(
    H264SliceHeader SliceHeader,
    int MacroblocksDecoded,
    int FinalQpY,
    int BitsConsumed,
    IReadOnlyList<H264InterMacroblockReconstructionResult> Macroblocks)
{
    public IReadOnlyList<uint> IntraMacroblockAddresses { get; init; } = [];

    /// <summary>Ordered reference metadata used by this slice when decoded through the DPB overload.</summary>
    public IReadOnlyList<H264ReferencePicture>? ReferenceList0 { get; init; }

    /// <summary>Recent CABAC syntax annotations, populated by the built-in diagnostic reader.</summary>
    public IReadOnlyList<string> CabacDiagnostics { get; init; } = [];
}

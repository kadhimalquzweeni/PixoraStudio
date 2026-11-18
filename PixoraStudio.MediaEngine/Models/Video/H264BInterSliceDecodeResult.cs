using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Summary of a decoded B slice, including inter and intra macroblocks.</summary>
public sealed record H264BInterSliceDecodeResult(
    H264SliceHeader SliceHeader,
    int MacroblocksDecoded,
    int FinalQpY,
    int BitsConsumed,
    IReadOnlyList<H264BInterMacroblockReconstructionResult> InterMacroblocks,
    IReadOnlyList<uint> IntraMacroblockAddresses)
{
    /// <summary>Ordered list-0/list-1 reference metadata used to decode this B slice.</summary>
    public H264BReferencePictureLists? ReferencePictureLists { get; init; }
}

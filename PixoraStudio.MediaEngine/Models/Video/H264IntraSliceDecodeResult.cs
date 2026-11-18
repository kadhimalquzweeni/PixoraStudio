using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Summary of one completely decoded CAVLC I slice.</summary>
public sealed record H264IntraSliceDecodeResult(
    H264SliceHeader SliceHeader,
    int MacroblocksDecoded,
    int FinalQpY,
    int BitsConsumed);

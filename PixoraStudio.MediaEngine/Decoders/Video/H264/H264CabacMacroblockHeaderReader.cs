using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Builds the existing macroblock-header model from CABAC I-slice mb_type bins.</summary>
public sealed class H264CabacMacroblockHeaderReader
{
    public H264MacroblockHeader ReadIntraSlice(
        H264CabacSyntaxElementReader syntaxReader,
        uint macroblockAddress,
        int firstBinContextIncrement)
    {
        ArgumentNullException.ThrowIfNull(syntaxReader);
        uint mbTypeCode = checked((uint)syntaxReader.ReadIntraSliceMacroblockType(
            firstBinContextIncrement));
        return H264MacroblockHeaderReader.DecodeIntraMacroblockType(
            macroblockAddress, mbTypeCode);
    }
}

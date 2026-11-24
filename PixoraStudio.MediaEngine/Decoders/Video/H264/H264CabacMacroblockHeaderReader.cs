using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Builds the existing macroblock-header model from CABAC I-slice mb_type bins.</summary>
public sealed class H264CabacMacroblockHeaderReader
{
    /// <summary>Reads one P-slice macroblock header using CABAC syntax.</summary>
    public H264MacroblockHeader ReadPSlice(
        H264CabacSyntaxElementReader reader,
        uint macroblockAddress,
        int skipFlagContextIncrement)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.ReadMacroblockSkipFlag(isBSlice: false, skipFlagContextIncrement))
            return CreateSkippedHeader(macroblockAddress);

        int macroblockType = reader.ReadPSliceMacroblockType();
        if (macroblockType >= 5)
        {
            return H264MacroblockHeaderReader.DecodeIntraMacroblockType(
                macroblockAddress,
                checked((uint)(macroblockType - 5)),
                checked((uint)macroblockType));
        }

        return CreateInterHeader(macroblockAddress, macroblockType);
    }

    /// <summary>Reads one B-slice macroblock header using CABAC syntax.</summary>
    public H264MacroblockHeader ReadBSlice(
        H264CabacSyntaxElementReader reader,
        uint macroblockAddress,
        int skipFlagContextIncrement,
        int macroblockTypeContextIncrement)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.ReadMacroblockSkipFlag(isBSlice: true, skipFlagContextIncrement))
            return CreateSkippedHeader(macroblockAddress);

        int macroblockType = reader.ReadBSliceMacroblockType(
            macroblockTypeContextIncrement);
        if (macroblockType >= 23)
        {
            return H264MacroblockHeaderReader.DecodeIntraMacroblockType(
                macroblockAddress,
                checked((uint)(macroblockType - 23)),
                checked((uint)macroblockType));
        }

        return CreateInterHeader(macroblockAddress, macroblockType);
    }

    public int ReadPSubMacroblockType(H264CabacSyntaxElementReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.ReadPSubMacroblockType();
    }

    public int ReadBSubMacroblockType(H264CabacSyntaxElementReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.ReadBSubMacroblockType();
    }

    private static H264MacroblockHeader CreateSkippedHeader(uint macroblockAddress) =>
        new(macroblockAddress, 0, true, 1, false, false,
            null, null, null, false);

    private static H264MacroblockHeader CreateInterHeader(
        uint macroblockAddress, int macroblockType) =>
        new(macroblockAddress, checked((uint)macroblockType), false, 0,
            false, false, null, null, null, false);

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

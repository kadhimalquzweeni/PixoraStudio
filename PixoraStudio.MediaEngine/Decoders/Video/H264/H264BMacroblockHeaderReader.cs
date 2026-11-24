using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Reads the frame-coded CAVLC B-slice macroblock skip/type prefix.</summary>
public sealed class H264BMacroblockHeaderReader
{
    public H264MacroblockHeader ReadNext(
        H264BitReader reader,
        uint macroblockAddress,
        uint pictureMacroblockCount,
        bool entropyCodingModeFlag = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (entropyCodingModeFlag)
            throw new NotSupportedException("CABAC B macroblock parsing is not implemented.");
        if (pictureMacroblockCount == 0 || macroblockAddress >= pictureMacroblockCount)
            throw new ArgumentOutOfRangeException(nameof(macroblockAddress));

        uint skipRun = reader.ReadUnsignedExpGolomb();
        if (skipRun > pictureMacroblockCount - macroblockAddress)
            throw new FormatException("mb_skip_run exceeds the remaining B-slice macroblocks.");
        if (skipRun != 0)
            return new H264MacroblockHeader(
                macroblockAddress, 0, true, skipRun,
                false, false, null, null, null, false);

        uint mbType = reader.ReadUnsignedExpGolomb();
        if (mbType <= 22)
            return new H264MacroblockHeader(
                macroblockAddress, mbType, false, 0,
                false, false, null, null, null, false);
        if (mbType > 48)
            throw new FormatException("B-slice mb_type must be in the range 0..48.");

        uint intraType = mbType - 23;
        if (intraType == 0)
            return new H264MacroblockHeader(
                macroblockAddress, mbType, false, 0,
                true, false, null, null, null, false);
        if (intraType is >= 1 and <= 24)
        {
            uint value = intraType - 1;
            return new H264MacroblockHeader(
                macroblockAddress, mbType, false, 0,
                true, true, value % 4,
                (value / 12) * 15, (value / 4) % 3, false);
        }
        return new H264MacroblockHeader(
            macroblockAddress, mbType, false, 0,
            true, false, null, null, null, true);
    }
}

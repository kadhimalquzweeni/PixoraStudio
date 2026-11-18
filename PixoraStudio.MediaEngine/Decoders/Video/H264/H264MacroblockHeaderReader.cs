using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads the skip-run and macroblock-type prefix for I and P slices.
/// This reader does not consume prediction or residual payload syntax.
/// </summary>
public sealed class H264MacroblockHeaderReader
{
    public H264MacroblockHeader ReadNext(
        H264BitReader reader,
        H264SliceType sliceType,
        uint macroblockAddress,
        uint pictureMacroblockCount,
        bool entropyCodingModeFlag = false)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (sliceType is not (H264SliceType.I or H264SliceType.P))
            throw new NotSupportedException("Macroblock prefix parsing currently supports I and P slices only.");
        if (entropyCodingModeFlag)
            throw new NotSupportedException("CABAC macroblock parsing is not implemented; use CAVLC slices.");
        if (pictureMacroblockCount == 0 || macroblockAddress >= pictureMacroblockCount)
            throw new ArgumentOutOfRangeException(nameof(macroblockAddress),
                "The macroblock address must be inside the picture macroblock range.");

        if (sliceType == H264SliceType.P)
        {
            uint skipRun = reader.ReadUnsignedExpGolomb();
            if (skipRun > pictureMacroblockCount - macroblockAddress)
                throw new FormatException("mb_skip_run exceeds the remaining macroblocks in the picture.");

            if (skipRun > 0)
            {
                return new H264MacroblockHeader(
                    macroblockAddress, 0, true, skipRun,
                    false, false, null, null, null, false);
            }
        }

        uint mbTypeCode = reader.ReadUnsignedExpGolomb();
        if (sliceType == H264SliceType.I)
            return DecodeIntraMacroblockType(macroblockAddress, mbTypeCode);

        if (mbTypeCode <= 4)
        {
            return new H264MacroblockHeader(
                macroblockAddress, mbTypeCode, false, 0,
                false, false, null, null, null, false);
        }

        return DecodeIntraMacroblockType(macroblockAddress, mbTypeCode - 5, mbTypeCode);
    }

    internal static H264MacroblockHeader DecodeIntraMacroblockType(
        uint address,
        uint intraTypeCode,
        uint? originalTypeCode = null)
    {
        uint typeCode = originalTypeCode ?? intraTypeCode;
        if (intraTypeCode == 0)
        {
            return new H264MacroblockHeader(
                address, typeCode, false, 0,
                true, false, null, null, null, false);
        }

        if (intraTypeCode is >= 1 and <= 24)
        {
            uint value = intraTypeCode - 1;
            uint predMode = value % 4;
            uint chromaPattern = (value / 4) % 3;
            uint lumaPattern = (value / 12) * 15;
            return new H264MacroblockHeader(
                address, typeCode, false, 0,
                true, true, predMode, lumaPattern, chromaPattern, false);
        }

        if (intraTypeCode == 25)
        {
            return new H264MacroblockHeader(
                address, typeCode, false, 0,
                true, false, null, null, null, true);
        }

        throw new FormatException("mb_type is outside the supported I/P macroblock type range.");
    }
}

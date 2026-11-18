using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Reads and validates the leading PPS syntax elements.</summary>
public sealed class H264PictureParameterSetReader
{
    public H264PictureParameterSetHeader Read(H264NalUnit nalUnit)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        if (nalUnit.NalUnitType != 8)
            throw new FormatException("The NAL unit is not a PPS.");

        H264BitReader reader = new(nalUnit.Rbsp);
        uint ppsId = ReadBoundedUe(reader, 255, "pic_parameter_set_id");
        uint spsId = ReadBoundedUe(reader, 31, "seq_parameter_set_id");
        bool entropyCodingMode = reader.ReadFlag();
        bool bottomFieldPicOrderPresent = reader.ReadFlag();
        uint sliceGroupsMinus1 = ReadBoundedUe(reader, 7, "num_slice_groups_minus1");

        return new H264PictureParameterSetHeader(
            ppsId, spsId, entropyCodingMode,
            bottomFieldPicOrderPresent, sliceGroupsMinus1);
    }

    private static uint ReadBoundedUe(H264BitReader reader, uint maximum, string field)
    {
        uint value = reader.ReadUnsignedExpGolomb();
        if (value > maximum)
            throw new FormatException($"PPS {field} is outside its permitted range.");
        return value;
    }
}

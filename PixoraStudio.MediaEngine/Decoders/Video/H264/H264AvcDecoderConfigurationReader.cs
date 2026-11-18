using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Extracts SPS/PPS NAL units and the NAL length-field width from an MP4
/// AVCDecoderConfigurationRecord (avcC).
/// </summary>
public sealed class H264AvcDecoderConfigurationReader
{
    private static readonly HashSet<byte> ProfilesWithAvcCExtension =
        [100, 110, 122, 144];

    public H264AvcDecoderConfiguration Read(Mp4CodecConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Codec != Mp4VideoCodec.Avc ||
            configuration.BoxType != "avcC")
            throw new ArgumentException(
                "The codec configuration must be an AVC avcC record.",
                nameof(configuration));

        return Read(configuration.Data);
    }

    public H264AvcDecoderConfiguration Read(ReadOnlyMemory<byte> configurationData)
    {
        ReadOnlySpan<byte> data = configurationData.Span;
        if (data.Length < 7)
            throw new FormatException("The AVC decoder configuration record is incomplete.");
        if (data[0] != 1)
            throw new FormatException("Only AVC decoder configuration version 1 is supported.");
        if ((data[4] & 0xFC) != 0xFC)
            throw new FormatException("The avcC length-size reserved bits must all be one.");
        if ((data[5] & 0xE0) != 0xE0)
            throw new FormatException("The avcC SPS-count reserved bits must all be one.");

        int lengthSize = (data[4] & 0x03) + 1;
        if (lengthSize == 3)
            throw new FormatException("The avcC record uses the reserved three-byte NAL length size.");

        byte profile = data[1];
        byte compatibility = data[2];
        byte level = data[3];
        int position = 6;
        int spsCount = data[5] & 0x1F;
        List<H264NalUnit> sequenceParameterSets = ReadNalUnits(
            data, ref position, spsCount, expectedNalType: 7, "SPS");

        if (position >= data.Length)
            throw new FormatException("The avcC record is missing numOfPictureParameterSets.");
        int ppsCount = data[position++];
        List<H264NalUnit> pictureParameterSets = ReadNalUnits(
            data, ref position, ppsCount, expectedNalType: 8, "PPS");

        byte? chromaFormat = null;
        byte? bitDepthLumaMinus8 = null;
        byte? bitDepthChromaMinus8 = null;
        List<H264NalUnit> sequenceParameterSetExtensions = [];

        if (position < data.Length)
        {
            if (!ProfilesWithAvcCExtension.Contains(profile))
                throw new FormatException(
                    "The avcC record contains extension fields for a profile without the defined AVC configuration extension.");
            if (data.Length - position < 4)
                throw new FormatException("The avcC high-profile extension is incomplete.");

            byte chroma = data[position++];
            byte lumaDepth = data[position++];
            byte chromaDepth = data[position++];
            if ((chroma & 0xFC) != 0xFC ||
                (lumaDepth & 0xF8) != 0xF8 ||
                (chromaDepth & 0xF8) != 0xF8)
                throw new FormatException("The avcC high-profile reserved bits must all be one.");

            chromaFormat = (byte)(chroma & 0x03);
            bitDepthLumaMinus8 = (byte)(lumaDepth & 0x07);
            bitDepthChromaMinus8 = (byte)(chromaDepth & 0x07);
            int extensionCount = data[position++];
            sequenceParameterSetExtensions = ReadNalUnits(
                data, ref position, extensionCount, expectedNalType: 13, "SPS extension");
        }

        if (position != data.Length)
            throw new FormatException("Unexpected bytes remain after the avcC configuration record.");

        return new H264AvcDecoderConfiguration(
            profile, compatibility, level, lengthSize,
            Array.AsReadOnly(sequenceParameterSets.ToArray()),
            Array.AsReadOnly(pictureParameterSets.ToArray()),
            Array.AsReadOnly(sequenceParameterSetExtensions.ToArray()))
        {
            ChromaFormat = chromaFormat,
            BitDepthLumaMinus8 = bitDepthLumaMinus8,
            BitDepthChromaMinus8 = bitDepthChromaMinus8
        };
    }

    private static List<H264NalUnit> ReadNalUnits(
        ReadOnlySpan<byte> data,
        ref int position,
        int count,
        byte expectedNalType,
        string description)
    {
        List<H264NalUnit> units = new(count);
        for (int i = 0; i < count; i++)
        {
            if (data.Length - position < 2)
                throw new FormatException($"The avcC {description} length is truncated.");

            ushort length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position, 2));
            position += 2;
            if (length < 2)
                throw new FormatException(
                    $"The avcC {description} NAL unit must contain a header and RBSP payload.");
            if (length > data.Length - position)
                throw new FormatException($"The avcC {description} NAL unit extends beyond the record.");

            ReadOnlySpan<byte> bytes = data.Slice(position, length);
            byte header = bytes[0];
            H264NalUnit unit = new(header, bytes[1..]);
            if (unit.NalUnitType != expectedNalType)
                throw new FormatException(
                    $"The avcC {description} array contains NAL unit type {unit.NalUnitType}, expected {expectedNalType}.");
            if (unit.NalRefIdc == 0)
                throw new FormatException(
                    $"The avcC {description} NAL unit must have non-zero nal_ref_idc.");

            units.Add(unit);
            position += length;
        }

        return units;
    }
}

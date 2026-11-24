using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads supported AAC SCE and CPE elements from a raw_data_block.
/// Optional fill, data-stream, and program-config elements may surround the audio element.
/// </summary>
public sealed class AacRawDataBlockReader
{
    private const int SingleChannelElementId = 0;
    private const int ChannelPairElementId = 1;
    private const int DataStreamElementId = 4;
    private const int ProgramConfigElementId = 5;
    private const int FillElementId = 6;
    private const int EndElementId = 7;

    private readonly AacIndividualChannelStreamDataReader _channelStreamReader = new();
    private readonly AacChannelPairElementReader _channelPairReader = new();

    public AacSingleChannelElement ReadSingleChannelElement(
        ReadOnlyMemory<byte> accessUnit,
        int samplingFrequencyIndex)
    {
        AacBitReader reader = CreateReader(accessUnit);
        int elementId = ReadNextAudioElementId(reader);

        if (elementId != SingleChannelElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block element {elementId} is not an SCE. A single-channel element is required.");
        }

        byte instanceTag = (byte)reader.ReadBits(4);
        AacIndividualChannelStreamData channelStream =
            _channelStreamReader.Read(reader, samplingFrequencyIndex);

        ReadEndMarkerAndConsumePadding(reader);
        return new AacSingleChannelElement(instanceTag, channelStream);
    }

    public AacChannelPairElement ReadChannelPairElement(
        ReadOnlyMemory<byte> accessUnit,
        int samplingFrequencyIndex)
    {
        AacBitReader reader = CreateReader(accessUnit);
        int elementId = ReadNextAudioElementId(reader);

        if (elementId != ChannelPairElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block element {elementId} is not a CPE. A channel-pair element is required.");
        }

        AacChannelPairElement pair =
            _channelPairReader.ReadAfterElementId(reader, samplingFrequencyIndex);

        ReadEndMarkerAndConsumePadding(reader);
        return pair;
    }

    private static AacBitReader CreateReader(ReadOnlyMemory<byte> accessUnit)
    {
        if (accessUnit.IsEmpty)
            throw new EndOfStreamException("The AAC access unit is empty.");

        return new AacBitReader(accessUnit);
    }

    private static int ReadNextAudioElementId(AacBitReader reader)
    {
        while (true)
        {
            int elementId = (int)reader.ReadBits(3);

            switch (elementId)
            {
                case FillElementId:
                    SkipFillElementPayload(reader);
                    continue;
                case DataStreamElementId:
                    SkipDataStreamElementPayload(reader);
                    continue;
                case ProgramConfigElementId:
                    SkipProgramConfigElementPayload(reader);
                    continue;
                default:
                    return elementId;
            }
        }
    }

    private static void SkipFillElementPayload(AacBitReader reader)
    {
        int count = (int)reader.ReadBits(4);

        if (count == 15)
        {
            int escapeCount = (int)reader.ReadBits(8);
            count += escapeCount - 1;
        }

        reader.SkipBits(checked(count * 8));
    }

    private static void SkipDataStreamElementPayload(AacBitReader reader)
    {
        _ = reader.ReadBits(4);
        bool byteAlign = reader.ReadBits(1) != 0;
        int count = (int)reader.ReadBits(8);

        if (count == 255)
            count = checked(count + (int)reader.ReadBits(8));

        if (byteAlign)
        {
            while (!reader.IsByteAligned)
            {
                if (reader.ReadBits(1) != 1)
                    throw new InvalidDataException(
                        "AAC data-stream element byte-alignment bits must be one.");
            }
        }

        reader.SkipBits(checked(count * 8));
    }

    private static void SkipProgramConfigElementPayload(AacBitReader reader)
    {
        _ = reader.ReadBits(4);
        _ = reader.ReadBits(2);
        _ = reader.ReadBits(4);

        int frontElementCount = (int)reader.ReadBits(4);
        int sideElementCount = (int)reader.ReadBits(4);
        int backElementCount = (int)reader.ReadBits(4);
        int lfeElementCount = (int)reader.ReadBits(2);
        int associatedDataElementCount = (int)reader.ReadBits(3);
        int validCcElementCount = (int)reader.ReadBits(4);

        if (reader.ReadBits(1) != 0)
            _ = reader.ReadBits(4);

        if (reader.ReadBits(1) != 0)
            _ = reader.ReadBits(4);

        if (reader.ReadBits(1) != 0)
            _ = reader.ReadBits(3);

        SkipChannelElementDescriptions(reader, frontElementCount);
        SkipChannelElementDescriptions(reader, sideElementCount);
        SkipChannelElementDescriptions(reader, backElementCount);

        reader.SkipBits(checked(lfeElementCount * 4));
        reader.SkipBits(checked(associatedDataElementCount * 4));
        reader.SkipBits(checked(validCcElementCount * 5));
        reader.AlignToByte();

        int commentFieldBytes = (int)reader.ReadBits(8);
        int commentBits = checked(commentFieldBytes * 8);

        if (commentBits > reader.RemainingBits)
        {
            throw new InvalidDataException(
                $"AAC PCE comment declares {commentFieldBytes} bytes, but only " +
                $"{reader.RemainingBits / 8} complete bytes remain " +
                $"(position={reader.PositionBits}, length={reader.LengthBits}, " +
                $"front={frontElementCount}, side={sideElementCount}, back={backElementCount}, " +
                $"lfe={lfeElementCount}, associatedData={associatedDataElementCount}, cc={validCcElementCount}).");
        }

        reader.SkipBits(commentBits);
    }

    private static void SkipChannelElementDescriptions(
        AacBitReader reader,
        int elementCount)
    {
        for (int i = 0; i < elementCount; i++)
        {
            _ = reader.ReadBits(1);
            _ = reader.ReadBits(4);
        }
    }

    private static void ReadEndMarkerAndConsumePadding(AacBitReader reader)
    {
        while (true)
        {
            int nextElementId = (int)reader.ReadBits(3);

            switch (nextElementId)
            {
                case DataStreamElementId:
                    SkipDataStreamElementPayload(reader);
                    continue;
                case ProgramConfigElementId:
                    SkipProgramConfigElementPayload(reader);
                    continue;
                case FillElementId:
                    SkipFillElementPayload(reader);
                    continue;
                case EndElementId:
                    break;
                default:
                    throw new NotSupportedException(
                        $"AAC raw_data_block contains additional element {nextElementId}; " +
                        "only one SCE or CPE, optional FIL/DSE/PCE elements, and ID_END are currently supported.");
            }

            break;
        }

        if (reader.RemainingBits > 7)
            throw new InvalidDataException(
                "AAC raw_data_block contains data after its end marker.");

        // MP4 sample payloads are byte-sized. The final unused bits after ID_END
        // are transport padding, not AAC syntax, so do not interpret their values.
        reader.SkipBits(reader.RemainingBits);
    }
}

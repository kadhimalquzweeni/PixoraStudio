using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads supported AAC SCE and CPE elements from a raw_data_block.
/// Optional fill elements may appear before or after the audio element.
/// </summary>
public sealed class AacRawDataBlockReader
{
    private const int SingleChannelElementId = 0;
    private const int ChannelPairElementId = 1;
    private const int FillElementId = 6;
    private const int EndElementId = 7;

    private readonly AacIndividualChannelStreamDataReader _channelStreamReader = new();
    private readonly AacChannelPairElementReader _channelPairReader = new();

    public AacSingleChannelElement ReadSingleChannelElement(
        ReadOnlyMemory<byte> accessUnit,
        int samplingFrequencyIndex)
    {
        AacBitReader reader = CreateReader(accessUnit);
        int elementId = ReadNextNonFillElementId(reader);

        if (elementId != SingleChannelElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block element {elementId} is not an SCE. A single-channel element is required.");
        }

        byte instanceTag = (byte)reader.ReadBits(4);
        AacIndividualChannelStreamData channelStream =
            _channelStreamReader.Read(reader, samplingFrequencyIndex);

        ReadEndMarkerAndValidatePadding(reader);
        return new AacSingleChannelElement(instanceTag, channelStream);
    }

    public AacChannelPairElement ReadChannelPairElement(
        ReadOnlyMemory<byte> accessUnit,
        int samplingFrequencyIndex)
    {
        AacBitReader reader = CreateReader(accessUnit);
        int elementId = ReadNextNonFillElementId(reader);

        if (elementId != ChannelPairElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block element {elementId} is not a CPE. A channel-pair element is required.");
        }

        AacChannelPairElement pair =
            _channelPairReader.ReadAfterElementId(reader, samplingFrequencyIndex);

        ReadEndMarkerAndValidatePadding(reader);
        return pair;
    }

    private static AacBitReader CreateReader(ReadOnlyMemory<byte> accessUnit)
    {
        if (accessUnit.IsEmpty)
            throw new EndOfStreamException("The AAC access unit is empty.");

        return new AacBitReader(accessUnit);
    }

    private static int ReadNextNonFillElementId(AacBitReader reader)
    {
        while (true)
        {
            int elementId = (int)reader.ReadBits(3);
            if (elementId != FillElementId)
                return elementId;

            SkipFillElementPayload(reader);
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

    private static void ReadEndMarkerAndValidatePadding(AacBitReader reader)
    {
        int nextElementId = ReadNextNonFillElementId(reader);
        if (nextElementId != EndElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block contains additional element {nextElementId}; " +
                "only one SCE or CPE, optional fill elements, and ID_END are currently supported.");
        }

        if (reader.RemainingBits > 7)
            throw new InvalidDataException(
                "AAC raw_data_block contains data after its end marker.");

        while (reader.RemainingBits > 0)
        {
            if (reader.ReadBits(1) != 1)
                throw new InvalidDataException("AAC raw_data_block byte-alignment bits must be one.");
        }
    }
}

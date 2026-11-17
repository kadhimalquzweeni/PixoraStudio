using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads supported AAC SCE and CPE elements from a raw_data_block.
/// Each supported access unit contains one audio element followed by ID_END.
/// </summary>
public sealed class AacRawDataBlockReader
{
    private const int SingleChannelElementId = 0;
    private const int ChannelPairElementId = 1;
    private const int EndElementId = 7;

    private readonly AacIndividualChannelStreamDataReader _channelStreamReader = new();
    private readonly AacChannelPairElementReader _channelPairReader = new();

    public AacSingleChannelElement ReadSingleChannelElement(
        ReadOnlyMemory<byte> accessUnit,
        int samplingFrequencyIndex)
    {
        AacBitReader reader = CreateReader(accessUnit);
        int elementId = (int)reader.ReadBits(3);

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
        AacChannelPairElement pair =
            _channelPairReader.Read(reader, samplingFrequencyIndex);

        ReadEndMarkerAndValidatePadding(reader);
        return pair;
    }

    private static AacBitReader CreateReader(ReadOnlyMemory<byte> accessUnit)
    {
        if (accessUnit.IsEmpty)
            throw new EndOfStreamException("The AAC access unit is empty.");

        return new AacBitReader(accessUnit);
    }

    private static void ReadEndMarkerAndValidatePadding(AacBitReader reader)
    {
        int nextElementId = (int)reader.ReadBits(3);
        if (nextElementId != EndElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block contains additional element {nextElementId}; " +
                "only one SCE or CPE followed by ID_END is currently supported.");
        }

        while (reader.RemainingBits > 0)
        {
            if (reader.ReadBits(1) != 0)
                throw new InvalidDataException("AAC raw_data_block alignment padding must be zero.");
        }
    }
}

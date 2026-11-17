using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads the supported single-channel element form of an AAC raw_data_block.
/// The current MP4 playback path accepts one SCE followed by ID_END.
/// </summary>
public sealed class AacRawDataBlockReader
{
    private const int SingleChannelElementId = 0;
    private const int EndElementId = 7;

    private readonly AacIndividualChannelStreamDataReader _channelStreamReader = new();

    public AacSingleChannelElement ReadSingleChannelElement(
        ReadOnlyMemory<byte> accessUnit,
        int samplingFrequencyIndex)
    {
        if (accessUnit.IsEmpty)
            throw new EndOfStreamException("The AAC access unit is empty.");

        AacBitReader reader = new(accessUnit);
        int elementId = (int)reader.ReadBits(3);

        if (elementId != SingleChannelElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block element {elementId} is not supported by the mono decoder. " +
                "A single-channel element (SCE) is required.");
        }

        byte instanceTag = (byte)reader.ReadBits(4);
        AacIndividualChannelStreamData channelStream =
            _channelStreamReader.Read(reader, samplingFrequencyIndex);

        int nextElementId = (int)reader.ReadBits(3);
        if (nextElementId != EndElementId)
        {
            throw new NotSupportedException(
                $"AAC raw_data_block contains additional element {nextElementId}; " +
                "only one SCE followed by ID_END is currently supported.");
        }

        // Remaining bits in the final byte are byte_alignment() padding.
        while (reader.RemainingBits > 0)
        {
            if (reader.ReadBits(1) != 0)
                throw new InvalidDataException("AAC raw_data_block alignment padding must be zero.");
        }

        return new AacSingleChannelElement(instanceTag, channelStream);
    }
}

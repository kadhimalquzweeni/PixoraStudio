
namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Reads MPEG-4 AAC-LC frames stored in an ADTS container.
/// </summary>
public sealed class AacAdtsFrameReader
{
    private static readonly uint[] SamplingFrequencies =
    [
        96000,
        88200,
        64000,
        48000,
        44100,
        32000,
        24000,
        22050,
        16000,
        12000,
        11025,
        8000,
        7350
    ];

    private static readonly int[] ChannelCounts =
    [
        0, // Reserved: channel configuration 0 requires a PCE.
        1,
        2,
        3,
        4,
        5,
        6,
        8
    ];

    /// <summary>
    /// Reads the first ADTS frame from the supplied data.
    /// Trailing data is permitted and is not consumed.
    /// </summary>
    public AacAdtsFrame Read(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 7)
        {
            throw new EndOfStreamException(
                "The data does not contain a complete ADTS header.");
        }

        AacBitReader reader = new(data);

        uint syncWord = reader.ReadBits(12);

        if (syncWord != 0xFFF)
        {
            throw new InvalidDataException(
                "The ADTS sync word is invalid.");
        }

        byte mpegId = (byte)reader.ReadBits(1);
        uint layer = reader.ReadBits(2);
        bool protectionAbsent = reader.ReadBits(1) != 0;

        if (layer != 0)
        {
            throw new InvalidDataException(
                "The ADTS layer must be zero.");
        }

        // ADTS MPEG ID 0 identifies MPEG-4.
        if (mpegId != 0)
        {
            throw new NotSupportedException(
                "Only MPEG-4 ADTS streams are supported.");
        }

        byte profile = (byte)reader.ReadBits(2);

        // ADTS profile 1 represents AAC Low Complexity (AAC-LC).
        if (profile != 1)
        {
            throw new NotSupportedException(
                $"ADTS profile {profile} is not supported. " +
                "Only AAC-LC is currently supported.");
        }

        byte samplingFrequencyIndex =
            (byte)reader.ReadBits(4);

        if (samplingFrequencyIndex >= SamplingFrequencies.Length)
        {
            throw new InvalidDataException(
                "The ADTS sampling frequency index is reserved.");
        }

        uint samplingFrequency =
            SamplingFrequencies[samplingFrequencyIndex];

        reader.SkipBits(1); // Private bit.

        byte channelConfiguration =
            (byte)reader.ReadBits(3);

        if (channelConfiguration == 0)
        {
            throw new NotSupportedException(
                "ADTS channel configuration zero requires " +
                "a Program Config Element, which is not yet supported.");
        }

        if (channelConfiguration >= ChannelCounts.Length)
        {
            throw new InvalidDataException(
                "The ADTS channel configuration is reserved.");
        }

        int channelCount = ChannelCounts[channelConfiguration];

        reader.SkipBits(1); // Originality.
        reader.SkipBits(1); // Home.
        reader.SkipBits(1); // Copyright identification bit.
        reader.SkipBits(1); // Copyright identification start.

        int frameLength = (int)reader.ReadBits(13);

        reader.SkipBits(11); // ADTS buffer fullness.

        int rawDataBlockCount =
            checked((int)reader.ReadBits(2) + 1);

        if (rawDataBlockCount != 1)
        {
            throw new NotSupportedException(
                "ADTS frames containing multiple raw data blocks " +
                "are not currently supported.");
        }

        int headerLength = protectionAbsent ? 7 : 9;

        if (data.Length < headerLength)
        {
            throw new EndOfStreamException(
                "The ADTS header or CRC is incomplete.");
        }

        if (frameLength < headerLength)
        {
            throw new InvalidDataException(
                "The ADTS frame length is smaller than its header.");
        }

        if (frameLength > data.Length)
        {
            throw new EndOfStreamException(
                "The ADTS frame is incomplete.");
        }

        ReadOnlyMemory<byte> payload =
            data.Slice(headerLength, frameLength - headerLength);

        return new AacAdtsFrame(
            mpegId,
            profile,
            samplingFrequencyIndex,
            samplingFrequency,
            channelConfiguration,
            channelCount,
            headerLength,
            frameLength,
            rawDataBlockCount,
            hasCrc: !protectionAbsent,
            payload);
    }
}

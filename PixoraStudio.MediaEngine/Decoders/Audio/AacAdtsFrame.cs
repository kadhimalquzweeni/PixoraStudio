
namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Represents one MPEG-4 AAC frame carried in an ADTS container.
/// </summary>
public sealed class AacAdtsFrame
{
    public byte MpegId { get; }

    public byte Profile { get; }

    public byte SamplingFrequencyIndex { get; }

    public uint SamplingFrequency { get; }

    public byte ChannelConfiguration { get; }

    public int ChannelCount { get; }

    public int HeaderLength { get; }

    public int FrameLength { get; }

    public int RawDataBlockCount { get; }

    public bool HasCrc { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public AacAdtsFrame(
        byte mpegId,
        byte profile,
        byte samplingFrequencyIndex,
        uint samplingFrequency,
        byte channelConfiguration,
        int channelCount,
        int headerLength,
        int frameLength,
        int rawDataBlockCount,
        bool hasCrc,
        ReadOnlyMemory<byte> payload)
    {
        MpegId = mpegId;
        Profile = profile;
        SamplingFrequencyIndex = samplingFrequencyIndex;
        SamplingFrequency = samplingFrequency;
        ChannelConfiguration = channelConfiguration;
        ChannelCount = channelCount;
        HeaderLength = headerLength;
        FrameLength = frameLength;
        RawDataBlockCount = rawDataBlockCount;
        HasCrc = hasCrc;
        Payload = payload;
    }
}

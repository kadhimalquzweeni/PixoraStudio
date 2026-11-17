namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Represents the MPEG-4 AudioSpecificConfig used by an AAC stream.
/// </summary>
public sealed class Mp4AudioSpecificConfig
{
    private readonly byte[] _data;

    public Mp4AudioCodec Codec { get; }

    public byte AudioObjectType { get; }

    public uint SamplingFrequency { get; }

    public byte SamplingFrequencyIndex { get; }

    public byte ChannelConfiguration { get; }

    public int ChannelCount { get; }

    public IReadOnlyList<byte> Data =>
        Array.AsReadOnly(_data);

    public Mp4AudioSpecificConfig(
        Mp4AudioCodec codec,
        byte audioObjectType,
        uint samplingFrequency,
        byte samplingFrequencyIndex,
        byte channelConfiguration,
        int channelCount,
        byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (samplingFrequency == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(samplingFrequency));
        }

        if (channelCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelCount));
        }

        Codec = codec;
        AudioObjectType = audioObjectType;
        SamplingFrequency = samplingFrequency;
        SamplingFrequencyIndex = samplingFrequencyIndex;
        ChannelConfiguration = channelConfiguration;
        ChannelCount = channelCount;
        _data = data.ToArray();
    }
}
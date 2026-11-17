using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Models;

/// <summary>
/// Represents metadata extracted from an MP4 audio sample entry.
/// </summary>
public sealed class Mp4AudioSampleEntry
{
    public string Format { get; }

    public ushort DataReferenceIndex { get; }

    public ushort Version { get; }

    public ushort ChannelCount { get; }

    public ushort SampleSize { get; }

    public double SampleRate { get; }

    public Mp4AudioCodec Codec { get; }

    public Mp4AudioSpecificConfig? AudioSpecificConfig { get; }

    public Mp4AudioSampleEntry(
        string format,
        ushort dataReferenceIndex,
        ushort version,
        ushort channelCount,
        ushort sampleSize,
        double sampleRate,
        Mp4AudioCodec codec,
        Mp4AudioSpecificConfig? audioSpecificConfig)
    {
        ArgumentNullException.ThrowIfNull(format);

        Format = format;
        DataReferenceIndex = dataReferenceIndex;
        Version = version;
        ChannelCount = channelCount;
        SampleSize = sampleSize;
        SampleRate = sampleRate;
        Codec = codec;
        AudioSpecificConfig = audioSpecificConfig;
    }
}
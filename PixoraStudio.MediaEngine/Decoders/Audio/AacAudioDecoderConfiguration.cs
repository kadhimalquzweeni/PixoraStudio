
using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Contains validated configuration required to decode an AAC-LC stream.
/// </summary>
public sealed class AacAudioDecoderConfiguration
{
    public const int SamplesPerChannelPerFrame = 1024;

    public Mp4AudioSpecificConfig AudioSpecificConfig { get; }

    public byte AudioObjectType { get; }

    public uint SamplingFrequency { get; }

    public byte SamplingFrequencyIndex { get; }

    /// <summary>Sampling-frequency table row used for AAC scalefactor-band boundaries.</summary>
    public int ScalefactorBandSamplingFrequencyIndex { get; }

    public byte ChannelConfiguration { get; }

    public int ChannelCount { get; }

    public int SampleRate => checked((int)SamplingFrequency);

    private AacAudioDecoderConfiguration(
        Mp4AudioSpecificConfig audioSpecificConfig,
        int channelCount)
    {
        AudioSpecificConfig = audioSpecificConfig;
        AudioObjectType = audioSpecificConfig.AudioObjectType;
        SamplingFrequency = audioSpecificConfig.SamplingFrequency;
        SamplingFrequencyIndex = audioSpecificConfig.SamplingFrequencyIndex;
        ScalefactorBandSamplingFrequencyIndex = ResolveScalefactorBandIndex(
            audioSpecificConfig.SamplingFrequency,
            audioSpecificConfig.SamplingFrequencyIndex);
        ChannelConfiguration = audioSpecificConfig.ChannelConfiguration;
        ChannelCount = channelCount;
    }

    /// <summary>
    /// Creates a decoder configuration from an MPEG-4 AudioSpecificConfig.
    /// </summary>
    public static AacAudioDecoderConfiguration Create(
        Mp4AudioSpecificConfig audioSpecificConfig)
    {
        ArgumentNullException.ThrowIfNull(audioSpecificConfig);

        if (audioSpecificConfig.Codec != Mp4AudioCodec.Aac)
        {
            throw new NotSupportedException(
                $"Codec '{audioSpecificConfig.Codec}' is not supported.");
        }

        if (audioSpecificConfig.AudioObjectType != 2)
        {
            throw new NotSupportedException(
                $"AAC Audio Object Type {audioSpecificConfig.AudioObjectType} " +
                "is not supported. Only AAC-LC (Audio Object Type 2) is supported.");
        }

        if (audioSpecificConfig.SamplingFrequency == 0 ||
            audioSpecificConfig.SamplingFrequency > int.MaxValue)
        {
            throw new InvalidDataException(
                "The AAC sampling frequency is invalid.");
        }

        int channelCount = GetChannelCount(
            audioSpecificConfig.ChannelConfiguration);

        if (audioSpecificConfig.ChannelCount != channelCount)
        {
            throw new InvalidDataException(
                "The AAC channel count does not match its channel configuration.");
        }

        return new AacAudioDecoderConfiguration(
            audioSpecificConfig,
            channelCount);
    }

    private static int ResolveScalefactorBandIndex(uint samplingFrequency, byte samplingFrequencyIndex)
    {
        if (samplingFrequencyIndex <= 12)
            return samplingFrequencyIndex;

        // Preserve the explicit output rate while selecting the nearest
        // standard SWB table row supported by this decoder.
        ReadOnlySpan<uint> standardFrequencies =
        [96000, 88200, 64000, 48000, 44100, 32000, 24000,
         22050, 16000, 12000, 11025, 8000, 7350];

        int nearestIndex = 0;
        ulong smallestDifference = ulong.MaxValue;
        for (int i = 0; i < standardFrequencies.Length; i++)
        {
            ulong difference = samplingFrequency >= standardFrequencies[i]
                ? samplingFrequency - standardFrequencies[i]
                : standardFrequencies[i] - samplingFrequency;
            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                nearestIndex = i;
            }
        }
        return nearestIndex;
    }

    private static int GetChannelCount(byte channelConfiguration)
    {
        return channelConfiguration switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 8,

            0 => throw new NotSupportedException(
                "AAC channel configuration zero requires a Program Config Element, " +
                "which is not yet supported."),

            _ => throw new InvalidDataException(
                $"AAC channel configuration {channelConfiguration} is reserved.")
        };
    }
}

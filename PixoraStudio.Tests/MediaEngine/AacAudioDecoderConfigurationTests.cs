
using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacAudioDecoderConfigurationTests
{
    [Fact]
    public void Create_ShouldCreateStereoAacLcConfiguration()
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            CreateConfiguration(
                audioObjectType: 2,
                samplingFrequency: 44100,
                channelConfiguration: 2,
                channelCount: 2);

        AacAudioDecoderConfiguration result =
            AacAudioDecoderConfiguration.Create(audioSpecificConfig);

        Assert.Same(audioSpecificConfig, result.AudioSpecificConfig);
        Assert.Equal((byte)2, result.AudioObjectType);
        Assert.Equal(44100u, result.SamplingFrequency);
        Assert.Equal(44100, result.SampleRate);
        Assert.Equal((byte)4, result.SamplingFrequencyIndex);
        Assert.Equal((byte)2, result.ChannelConfiguration);
        Assert.Equal(2, result.ChannelCount);
        Assert.Equal(
            1024,
            AacAudioDecoderConfiguration.SamplesPerChannelPerFrame);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 8)]
    public void Create_ShouldResolveChannelConfigurations(
        byte channelConfiguration,
        int expectedChannelCount)
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            CreateConfiguration(
                audioObjectType: 2,
                samplingFrequency: 48000,
                channelConfiguration: channelConfiguration,
                channelCount: expectedChannelCount);

        AacAudioDecoderConfiguration result =
            AacAudioDecoderConfiguration.Create(audioSpecificConfig);

        Assert.Equal(expectedChannelCount, result.ChannelCount);
    }

    [Fact]
    public void Create_ShouldAcceptExplicitSamplingFrequency()
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            new(
                Mp4AudioCodec.Aac,
                audioObjectType: 2,
                samplingFrequency: 12345,
                samplingFrequencyIndex: 15,
                channelConfiguration: 1,
                channelCount: 1,
                data: [0x17, 0x88]);

        AacAudioDecoderConfiguration result =
            AacAudioDecoderConfiguration.Create(audioSpecificConfig);

        Assert.Equal(12345, result.SampleRate);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(17)]
    public void Create_ShouldRejectUnsupportedAudioObjectType(byte objectType)
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            CreateConfiguration(
                audioObjectType: objectType,
                samplingFrequency: 44100,
                channelConfiguration: 2,
                channelCount: 2);

        Assert.Throws<NotSupportedException>(
            () => AacAudioDecoderConfiguration.Create(audioSpecificConfig));
    }

    [Fact]
    public void Create_ShouldRejectNonAacCodec()
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            new(
                Mp4AudioCodec.Unknown,
                audioObjectType: 2,
                samplingFrequency: 44100,
                samplingFrequencyIndex: 4,
                channelConfiguration: 2,
                channelCount: 2,
                data: [0x12, 0x10]);

        Assert.Throws<NotSupportedException>(
            () => AacAudioDecoderConfiguration.Create(audioSpecificConfig));
    }

    [Fact]
    public void Create_ShouldRejectNullConfiguration()
    {
        Assert.Throws<ArgumentNullException>(
            () => AacAudioDecoderConfiguration.Create(null!));
    }

    [Fact]
    public void Create_ShouldRejectZeroSamplingFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Mp4AudioSpecificConfig(
                Mp4AudioCodec.Aac,
                2,
                0,
                4,
                2,
                2,
                [0x12, 0x10]));
    }

    [Fact]
    public void Create_ShouldRejectChannelConfigurationZero()
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            CreateConfiguration(
                audioObjectType: 2,
                samplingFrequency: 44100,
                channelConfiguration: 0,
                channelCount: 0);

        Assert.Throws<NotSupportedException>(
            () => AacAudioDecoderConfiguration.Create(audioSpecificConfig));
    }

    [Fact]
    public void Create_ShouldRejectReservedChannelConfiguration()
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            CreateConfiguration(
                audioObjectType: 2,
                samplingFrequency: 44100,
                channelConfiguration: 8,
                channelCount: 0);

        Assert.Throws<InvalidDataException>(
            () => AacAudioDecoderConfiguration.Create(audioSpecificConfig));
    }

    [Fact]
    public void Create_ShouldRejectMismatchedChannelCount()
    {
        Mp4AudioSpecificConfig audioSpecificConfig =
            CreateConfiguration(
                audioObjectType: 2,
                samplingFrequency: 44100,
                channelConfiguration: 2,
                channelCount: 1);

        Assert.Throws<InvalidDataException>(
            () => AacAudioDecoderConfiguration.Create(audioSpecificConfig));
    }

    private static Mp4AudioSpecificConfig CreateConfiguration(
        byte audioObjectType,
        uint samplingFrequency,
        byte channelConfiguration,
        int channelCount)
    {
        return new Mp4AudioSpecificConfig(
            Mp4AudioCodec.Aac,
            audioObjectType,
            samplingFrequency,
            samplingFrequencyIndex: 4,
            channelConfiguration,
            channelCount,
            [0x12, 0x10]);
    }
}

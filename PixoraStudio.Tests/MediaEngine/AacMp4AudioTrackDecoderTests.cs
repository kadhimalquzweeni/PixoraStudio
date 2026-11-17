using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacMp4AudioTrackDecoderTests
{
    [Fact]
    public void DecodeSample_ShouldDecodeMonoMp4AacAccessUnitToPcm()
    {
        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac, 2, 44100, 4, 1, 1, [0x12, 0x08]);
        Mp4AudioSampleEntry entry = new(
            "mp4a", 1, 0, 1, 16, 44100, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: entry);
        Mp4EncodedSample sample = CreateSample(
            AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit());

        PcmAudioBuffer result = new AacMp4AudioTrackDecoder()
            .DecodeSample(sample, description);

        Assert.Equal(44100, result.Format.SampleRate);
        Assert.Equal(1, result.Format.ChannelCount);
        Assert.Equal(1024, result.FrameCount);
        Assert.All(result.Samples.ToArray(), value => Assert.Equal(0f, value));
    }

    [Fact]
    public void DecodeSample_ShouldDecodeStereoMp4AacAccessUnitToInterleavedPcm()
    {
        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac, 2, 44100, 4, 2, 2, [0x12, 0x10]);
        Mp4AudioSampleEntry entry = new(
            "mp4a", 1, 0, 2, 16, 44100, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: entry);
        Mp4EncodedSample sample = CreateSample(
            AacRawDataBlockReaderTests.BuildEmptyStereoAccessUnit());

        PcmAudioBuffer result = new AacMp4AudioTrackDecoder()
            .DecodeSample(sample, description);

        Assert.Equal(44100, result.Format.SampleRate);
        Assert.Equal(2, result.Format.ChannelCount);
        Assert.Equal(1024, result.FrameCount);
        Assert.Equal(2048, result.SampleCount);
        Assert.All(result.Samples.ToArray(), value => Assert.Equal(0f, value));
    }

    [Theory]
    [InlineData(96000, 0, 1)]
    [InlineData(96000, 0, 2)]
    [InlineData(48000, 3, 1)]
    [InlineData(48000, 3, 2)]
    [InlineData(32000, 5, 1)]
    [InlineData(32000, 5, 2)]
    [InlineData(24000, 6, 1)]
    [InlineData(24000, 6, 2)]
    [InlineData(16000, 8, 1)]
    [InlineData(16000, 8, 2)]
    [InlineData(8000, 11, 1)]
    [InlineData(8000, 11, 2)]
    [InlineData(7350, 12, 1)]
    [InlineData(7350, 12, 2)]
    public void DecodeSample_ShouldSupportStandardSamplingFrequencies(
        uint sampleRate,
        byte samplingFrequencyIndex,
        int channelCount)
    {
        byte channelConfiguration = (byte)channelCount;
        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac,
            2,
            sampleRate,
            samplingFrequencyIndex,
            channelConfiguration,
            channelCount,
            [0x12, 0x10]);
        Mp4AudioSampleEntry entry = new(
            "mp4a", 1, 0, checked((ushort)channelCount), 16,
            sampleRate, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: entry);
        byte[] accessUnit = channelCount == 1
            ? AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit()
            : AacRawDataBlockReaderTests.BuildEmptyStereoAccessUnit();

        PcmAudioBuffer result = new AacMp4AudioTrackDecoder()
            .DecodeSample(CreateSample(accessUnit), description);

        Assert.Equal((int)sampleRate, result.Format.SampleRate);
        Assert.Equal(channelCount, result.Format.ChannelCount);
        Assert.Equal(1024, result.FrameCount);
        Assert.Equal(1024 * channelCount, result.SampleCount);
        Assert.All(result.Samples.ToArray(), value => Assert.Equal(0f, value));
    }

    private static Mp4EncodedSample CreateSample(byte[] payload)
    {
        Mp4SampleLocation location = new(
            1, 1, 0, checked((uint)payload.Length), 0, 1024, true)
        {
            SampleDescriptionIndex = 1
        };

        return new Mp4EncodedSample(location, payload);
    }
}

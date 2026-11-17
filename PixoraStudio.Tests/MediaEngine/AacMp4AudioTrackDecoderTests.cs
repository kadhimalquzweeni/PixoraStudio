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
    [InlineData(false)]
    [InlineData(true)]
    public void DecodeSample_ShouldRemainUsableAfterTruncatedAccessUnit(bool stereo)
    {
        int channelCount = stereo ? 2 : 1;
        byte channelConfiguration = checked((byte)channelCount);
        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac, 2, 44100, 4,
            channelConfiguration, channelCount,
            stereo ? [0x12, 0x10] : [0x12, 0x08]);
        Mp4AudioSampleEntry entry = new(
            "mp4a", 1, 0, checked((ushort)channelCount), 16,
            44100, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: entry);
        byte[] validAccessUnit = stereo
            ? AacRawDataBlockReaderTests.BuildEmptyStereoAccessUnit()
            : AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit();
        byte[] truncatedAccessUnit = validAccessUnit[..^1];
        AacMp4AudioTrackDecoder decoder = new();

        Assert.Throws<EndOfStreamException>(() =>
            decoder.DecodeSample(CreateSample(truncatedAccessUnit), description));

        PcmAudioBuffer decoded = decoder.DecodeSample(
            CreateSample(validAccessUnit),
            description);

        Assert.Equal(44100, decoded.Format.SampleRate);
        Assert.Equal(channelCount, decoded.Format.ChannelCount);
        Assert.Equal(1024, decoded.FrameCount);
    }

    [Theory]
    [InlineData(96000, 0, 1)]
    [InlineData(96000, 0, 2)]
    [InlineData(88200, 1, 1)]
    [InlineData(88200, 1, 2)]
    [InlineData(64000, 2, 1)]
    [InlineData(64000, 2, 2)]
    [InlineData(48000, 3, 1)]
    [InlineData(48000, 3, 2)]
    [InlineData(44100, 4, 1)]
    [InlineData(44100, 4, 2)]
    [InlineData(32000, 5, 1)]
    [InlineData(32000, 5, 2)]
    [InlineData(24000, 6, 1)]
    [InlineData(24000, 6, 2)]
    [InlineData(22050, 7, 1)]
    [InlineData(22050, 7, 2)]
    [InlineData(16000, 8, 1)]
    [InlineData(16000, 8, 2)]
    [InlineData(12000, 9, 1)]
    [InlineData(12000, 9, 2)]
    [InlineData(11025, 10, 1)]
    [InlineData(11025, 10, 2)]
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

    [Theory]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 5)]
    [InlineData(6, 6)]
    [InlineData(7, 8)]
    public void DecodeSample_ShouldRejectUnsupportedMultichannelLayouts(
        byte channelConfiguration,
        int channelCount)
    {
        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac, 2, 44100, 4,
            channelConfiguration, channelCount, [0x12, 0x10]);
        Mp4AudioSampleEntry entry = new(
            "mp4a", 1, 0, checked((ushort)channelCount), 16,
            44100, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: entry);
        Mp4EncodedSample sample = CreateSample(
            AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit());

        Assert.Throws<NotSupportedException>(
            () => new AacMp4AudioTrackDecoder().DecodeSample(sample, description));
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

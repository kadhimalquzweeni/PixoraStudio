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

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
            Mp4AudioCodec.Aac,
            audioObjectType: 2,
            samplingFrequency: 44100,
            samplingFrequencyIndex: 4,
            channelConfiguration: 1,
            channelCount: 1,
            data: [0x12, 0x08]);

        Mp4AudioSampleEntry entry = new(
            "mp4a", 1, 0, 1, 16, 44100,
            Mp4AudioCodec.Aac,
            config);

        Mp4SampleDescription description = new(
            1, "mp4a", audio: entry);

        Mp4SampleLocation location = new(
            1, 1, 0, 4, 0, 1024, true)
        {
            SampleDescriptionIndex = 1
        };

        Mp4EncodedSample sample = new(
            location,
            AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit());

        PcmAudioBuffer result = new AacMp4AudioTrackDecoder()
            .DecodeSample(sample, description);

        Assert.Equal(44100, result.Format.SampleRate);
        Assert.Equal(1, result.Format.ChannelCount);
        Assert.Equal(1024, result.FrameCount);
        Assert.All(result.Samples.ToArray(), value => Assert.Equal(0f, value));
    }

    [Fact]
    public void DecodeSample_ShouldRejectStereoConfigurationUntilCpePathIsAvailable()
    {
        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac, 2, 44100, 4, 2, 2, [0x12, 0x10]);
        Mp4AudioSampleEntry entry = new(
            "mp4a", 1, 0, 2, 16, 44100, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: entry);
        Mp4EncodedSample sample = new(
            new Mp4SampleLocation(1, 1, 0, 4, 0, 1024, true)
            {
                SampleDescriptionIndex = 1
            },
            AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit());

        Assert.Throws<NotSupportedException>(() =>
            new AacMp4AudioTrackDecoder().DecodeSample(sample, description));
    }
}

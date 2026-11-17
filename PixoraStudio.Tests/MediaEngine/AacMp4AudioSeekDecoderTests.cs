using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacMp4AudioSeekDecoderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decode_ShouldRestorePrerollAndReturnTimelineFromRequestedTime(bool stereo)
    {
        byte[] accessUnit = stereo
            ? AacRawDataBlockReaderTests.BuildEmptyStereoAccessUnit()
            : AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit();
        int channels = stereo ? 2 : 1;
        byte[] fileData = accessUnit.Concat(accessUnit).Concat(accessUnit).ToArray();

        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac, 2, 44100, 4,
            checked((byte)channels), channels,
            stereo ? [0x12, 0x10] : [0x12, 0x08]);
        Mp4AudioSampleEntry audioEntry = new(
            "mp4a", 1, 0, checked((ushort)channels),
            16, 44100, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: audioEntry);
        Mp4Track metadata = new(
            1, 0, 0, 44100, 3072,
            Mp4HandlerType.Audio,
            [description]);

        Mp4SampleLocation[] locations =
        [
            CreateLocation(1, 0, accessUnit.Length),
            CreateLocation(2, checked((ulong)accessUnit.Length), accessUnit.Length),
            CreateLocation(3, checked((ulong)accessUnit.Length * 2), accessUnit.Length)
        ];

        Mp4MediaTrack track = new(
            metadata,
            new Mp4Box(8, "trak", 0, 8),
            new Mp4SampleTable(
                [new Mp4TimeToSampleEntry(3, 1024)],
                [new Mp4SampleToChunkEntry(1, 3, 1)],
                new Mp4SampleSizeTable(checked((uint)accessUnit.Length), 0, []),
                new Mp4ChunkOffsetTable([0]),
                null),
            new Mp4SampleLocationTable(locations));

        Mp4PresentationSample[] presentation =
            locations.Select(location => new Mp4PresentationSample(
                1,
                location.SampleNumber,
                location,
                (double)location.PresentationTimeTicks / 44100)).ToArray();

        Mp4MediaInfo mediaInfo = new(
            new Mp4MovieHeader(44100, 3072),
            [track],
            [new Mp4MediaData(0, fileData.Length)],
            presentation);

        double targetTime = (1024d + 128d) / 44100d;
        AacMp4AudioSeekDecoder decoder = new();

        using MemoryStream sampleStream = new(fileData);
        Mp4DecodedAudioSample[] decodedSamples = decoder.DecodeSamples(
            sampleStream,
            mediaInfo,
            1,
            targetTime).ToArray();

        Assert.Equal(2, decodedSamples.Length);
        Assert.Equal(2u, decodedSamples[0].SampleNumber);
        Assert.Equal(0, decodedSamples[0].PresentationStartFrame);
        Assert.Equal(896, decodedSamples[0].Buffer.FrameCount);
        Assert.Equal(3u, decodedSamples[1].SampleNumber);
        Assert.Equal(896, decodedSamples[1].PresentationStartFrame);
        Assert.Equal(1024, decodedSamples[1].Buffer.FrameCount);

        using MemoryStream stream = new(fileData);
        var result = decoder.Decode(stream, mediaInfo, 1, targetTime);

        Assert.Equal(1920, result.FrameCount);
        Assert.Equal(channels, result.Format.ChannelCount);
        Assert.All(result.Samples.ToArray(), sample => Assert.Equal(0f, sample));
    }

    private static Mp4SampleLocation CreateLocation(
        uint sampleNumber,
        ulong offset,
        int size) =>
        new(
            sampleNumber,
            1,
            offset,
            checked((uint)size),
            (sampleNumber - 1) * 1024UL,
            1024,
            sampleNumber == 1)
        {
            SampleDescriptionIndex = 1
        };
}

using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacMp4AudioSeekTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DecodeSamplesFrom_ShouldDecodePrerollAndTrimTargetFrame(bool stereo)
    {
        byte[] accessUnit = stereo
            ? AacRawDataBlockReaderTests.BuildEmptyStereoAccessUnit()
            : AacRawDataBlockReaderTests.BuildEmptyMonoAccessUnit();
        int channels = stereo ? 2 : 1;
        byte[] fileData = accessUnit
            .Concat(accessUnit)
            .Concat(accessUnit)
            .ToArray();

        Mp4AudioSpecificConfig config = new(
            Mp4AudioCodec.Aac, 2, 44100, 4,
            checked((byte)channels), channels,
            stereo ? [0x12, 0x10] : [0x12, 0x08]);
        Mp4AudioSampleEntry audioEntry = new(
            "mp4a", 1, 0, checked((ushort)channels),
            16, 44100, Mp4AudioCodec.Aac, config);
        Mp4SampleDescription description = new(1, "mp4a", audio: audioEntry);

        Mp4Track trackMetadata = new(
            1, 0, 0, 44100, 3072,
            Mp4HandlerType.Audio,
            [description]);

        Mp4SampleLocation[] locations =
        [
            CreateLocation(1, 0, accessUnit.Length),
            CreateLocation(2, checked((ulong)accessUnit.Length), accessUnit.Length),
            CreateLocation(3, checked((ulong)accessUnit.Length * 2), accessUnit.Length)
        ];

        Mp4SampleTable sampleTable = new(
            [new Mp4TimeToSampleEntry(3, 1024)],
            [new Mp4SampleToChunkEntry(1, 3, 1)],
            new Mp4SampleSizeTable(checked((uint)accessUnit.Length), 0, []),
            new Mp4ChunkOffsetTable([0]),
            null);

        Mp4MediaTrack track = new(
            trackMetadata,
            new Mp4Box(8, "trak", 0, 8),
            sampleTable,
            new Mp4SampleLocationTable(locations));

        Mp4AudioSeekPlan plan = new(
            FirstSampleToDecode: 1,
            TargetSampleNumber: 2,
            FramesToSkipInTargetSample: 128,
            TargetPresentationTimeSeconds: 1.0,
            TargetSamplePresentationTimeSeconds: 1.0,
            IsAtEndOfTrack: false);

        using MemoryStream stream = new(fileData);
        PcmAudioBuffer[] decoded = new AacMp4AudioTrackDecoder()
            .DecodeSamplesFrom(
                stream,
                track,
                [new Mp4MediaData(0, fileData.Length)],
                plan)
            .ToArray();

        Assert.Equal(2, decoded.Length);
        Assert.Equal(1024 - 128, decoded[0].FrameCount);
        Assert.Equal(1024, decoded[1].FrameCount);
        Assert.Equal(channels, decoded[0].Format.ChannelCount);
        Assert.Equal(channels, decoded[1].Format.ChannelCount);
        Assert.All(decoded.SelectMany(buffer => buffer.Samples.ToArray()),
            sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void DecodeSamplesFrom_ShouldRejectSeekPlanOutsideTrack()
    {
        Mp4Track metadata = new(
            1, 0, 0, 44100, 0,
            Mp4HandlerType.Audio,
            []);
        Mp4MediaTrack track = new(
            metadata,
            new Mp4Box(8, "trak", 0, 8),
            new Mp4SampleTable([], [], new Mp4SampleSizeTable(1, 0, []),
                new Mp4ChunkOffsetTable([]), null),
            new Mp4SampleLocationTable([]));

        Mp4AudioSeekPlan plan = new(1, 1, 0, 0, 0, false);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AacMp4AudioTrackDecoder().DecodeSamplesFrom(
                new MemoryStream(),
                track,
                [],
                plan).ToArray());
    }

    private static Mp4SampleLocation CreateLocation(
        uint sampleNumber,
        ulong offset,
        int size)
    {
        return new Mp4SampleLocation(
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
}

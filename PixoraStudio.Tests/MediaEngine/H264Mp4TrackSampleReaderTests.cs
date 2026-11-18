using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4TrackSampleReaderTests
{
    [Fact]
    public void Read_ShouldResolveAvcDescriptionAndPreserveDecodeAndPresentationTimes()
    {
        Mp4Track track = Track();
        Mp4SampleLocation location = new(
            7, 2, 100, 8, 1000, 40, true)
        {
            CompositionOffsetTicks = 20,
            SampleDescriptionIndex = 1
        };
        Mp4EncodedSample sample = new(
            location,
            [0, 2, 0x65, 0x80, 0, 2, 0x41, 0x80]);

        H264Mp4VideoSample result =
            new H264Mp4TrackSampleReader().Read(track, sample);

        Assert.Equal(42u, result.TrackId);
        Assert.Equal(7u, result.SampleNumber);
        Assert.Equal(1.0, result.DecodeTimeSeconds, 6);
        Assert.Equal(1.02, result.PresentationTimeSeconds, 6);
        Assert.Equal(0.04, result.DurationSeconds, 6);
        Assert.True(result.IsSyncSample);
        Assert.Equal(2, result.Configuration.NaluLengthSize);
        Assert.Equal(new byte[] { 5, 1 },
            result.NalUnits.Select(unit => unit.NalUnitType).ToArray());
    }

    [Fact]
    public void Read_ShouldRejectMissingSampleDescription()
    {
        Mp4EncodedSample sample = new(
            Location(descriptionIndex: 2),
            [0, 2, 0x65, 0x80]);

        Assert.Throws<FormatException>(() =>
            new H264Mp4TrackSampleReader().Read(Track(), sample));
    }

    [Fact]
    public void Read_ShouldRejectNonVideoTrackAndNonAvcSampleDescription()
    {
        Mp4Track audioTrack = new(
            1, 0, 0, 1000, 1000, Mp4HandlerType.Audio, []);
        Assert.Throws<ArgumentException>(() =>
            new H264Mp4TrackSampleReader().Read(
                audioTrack, new Mp4EncodedSample(Location(), [0, 2, 0x65, 0x80])));

        Mp4VideoSampleEntry hevcEntry = new(
            "hvc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Hevc, new Mp4CodecConfiguration(
                Mp4VideoCodec.Hevc, "hvcC", new byte[23], 1));
        Mp4Track hevcTrack = new(
            1, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "hvc1", video: hevcEntry)]);

        Assert.Throws<NotSupportedException>(() =>
            new H264Mp4TrackSampleReader().Read(
                hevcTrack, new Mp4EncodedSample(Location(), [0, 2, 0x65, 0x80])));
    }

    [Fact]
    public void Read_ShouldRejectTrackWithoutTimescale()
    {
        Mp4Track track = new(
            42, 16, 16, 0, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: AvcEntry())]);

        Assert.Throws<FormatException>(() =>
            new H264Mp4TrackSampleReader().Read(
                track, new Mp4EncodedSample(Location(), [0, 2, 0x65, 0x80])));
    }

    private static Mp4Track Track() => new(
        42, 16, 16, 1000, 5000, Mp4HandlerType.Video,
        [new Mp4SampleDescription(1, "avc1", video: AvcEntry())]);

    private static Mp4VideoSampleEntry AvcEntry() => new(
        "avc1", 1, 16, 16, 72, 72, 1, "", 24,
        Mp4VideoCodec.Avc,
        new Mp4CodecConfiguration(
            Mp4VideoCodec.Avc, "avcC",
            [1, 66, 0, 30, 0xFD, 0xE1,
             0, 2, 0x67, 0x80, 1,
             0, 2, 0x68, 0x80],
            1, 66, 0, 30, 2));

    private static Mp4SampleLocation Location(uint descriptionIndex = 1) =>
        new(1, 1, 0, 4, 0, 40, true)
        {
            SampleDescriptionIndex = descriptionIndex
        };
}

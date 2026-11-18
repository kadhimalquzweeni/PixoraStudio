using PixoraStudio.Infrastructure.Services;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.Infrastructure;

public sealed class Mp4VideoInfoMapperTests
{
    [Fact]
    public void Map_ShouldUseVideoTrackMetadata()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo(5000, 125, 1920, 1080);

        var result = Mp4VideoInfoMapper.Map(mediaInfo, "clip.mp4");

        Assert.Equal("clip.mp4", result.FileName);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
        Assert.Equal(25, result.FramesPerSecond);
    }

    [Fact]
    public void Map_ShouldFallbackToMovieDurationWhenTrackDurationIsZero()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo(
            0, 100, 1280, 720, movieDurationTicks: 4000);

        var result = Mp4VideoInfoMapper.Map(mediaInfo, "clip.mp4");

        Assert.Equal(TimeSpan.FromSeconds(4), result.Duration);
        Assert.Equal(25, result.FramesPerSecond);
    }

    [Fact]
    public void Map_ShouldRejectMediaWithoutVideoTrack()
    {
        Mp4MediaInfo mediaInfo = new(
            new Mp4MovieHeader(1000, 1000),
            [CreateTrack(Mp4HandlerType.Audio, 1000, 1000, 0, 0, 0)],
            [],
            []);

        Assert.Throws<NotSupportedException>(
            () => Mp4VideoInfoMapper.Map(mediaInfo, "audio.mp4"));
    }

    [Fact]
    public void Map_ShouldRejectMissingDuration()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo(0, 0, 640, 480, 0);

        Assert.Throws<InvalidDataException>(
            () => Mp4VideoInfoMapper.Map(mediaInfo, "empty.mp4"));
    }

    [Fact]
    public void Map_ShouldRejectNullMediaInfo()
    {
        Assert.Throws<ArgumentNullException>(
            () => Mp4VideoInfoMapper.Map(null!, "clip.mp4"));
    }

    [Fact]
    public void Map_ShouldRejectEmptyPath()
    {
        Assert.Throws<ArgumentException>(
            () => Mp4VideoInfoMapper.Map(
                CreateMediaInfo(1000, 25, 640, 480), " "));
    }

    private static Mp4MediaInfo CreateMediaInfo(
        ulong durationTicks,
        uint sampleCount,
        ushort width,
        ushort height,
        ulong? movieDurationTicks = null)
    {
        return new Mp4MediaInfo(
            new Mp4MovieHeader(1000, movieDurationTicks ?? durationTicks),
            [CreateTrack(
                Mp4HandlerType.Video, 1000, durationTicks,
                sampleCount, width, height)],
            [],
            []);
    }

    private static Mp4MediaTrack CreateTrack(
        Mp4HandlerType handler,
        uint timescale,
        ulong durationTicks,
        uint sampleCount,
        ushort width,
        ushort height)
    {
        Mp4VideoSampleEntry? video = handler == Mp4HandlerType.Video
            ? new Mp4VideoSampleEntry(
                "avc1", 1, width, height, 72, 72, 1, "AVC", 24,
                Mp4VideoCodec.Avc, null)
            : null;

        Mp4Track track = new(
            1, width, height, timescale, durationTicks, handler,
            [new Mp4SampleDescription(
                1, handler == Mp4HandlerType.Video ? "avc1" : "mp4a", video)]);

        Mp4SampleTable table = new(
            [], [], new Mp4SampleSizeTable(1, 0, []),
            new Mp4ChunkOffsetTable([]), null);

        uint frameDuration = Math.Max(
            1u, timescale / Math.Max(1u, sampleCount));

        Mp4SampleLocation[] locations = Enumerable.Range(
                0, checked((int)sampleCount))
            .Select(index => new Mp4SampleLocation(
                checked((uint)index + 1),
                1,
                0,
                1,
                checked((ulong)index * frameDuration),
                frameDuration,
                index == 0))
            .ToArray();

        return new Mp4MediaTrack(
            track,
            new Mp4Box(8, "trak", 0, 8),
            table,
            new Mp4SampleLocationTable(locations));
    }
}

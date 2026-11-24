using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4TrackDecoderTests
{
    [Fact]
    public void DecodeSamples_EmptyVideoTrack_ShouldYieldNoFrames()
    {
        Mp4Track track = new(
            1, 16, 16, 1000, 0, Mp4HandlerType.Video, []);

        IReadOnlyList<H264Mp4DecodedVideoFrame> frames =
            new H264Mp4TrackDecoder().DecodeSamples(track, []).ToArray();

        Assert.Empty(frames);
    }

    [Fact]
    public void DecodeSamples_ShouldRejectAudioTrack()
    {
        Mp4Track track = new(
            1, 0, 0, 1000, 0, Mp4HandlerType.Audio, []);

        Assert.Throws<NotSupportedException>(() =>
            new H264Mp4TrackDecoder().DecodeSamples(track, []));
    }

    [Fact]
    public void DecodeSamples_ShouldRejectNullInputs()
    {
        H264Mp4TrackDecoder decoder = new();
        Mp4Track track = new(
            1, 16, 16, 1000, 0, Mp4HandlerType.Video, []);

        Assert.Throws<ArgumentNullException>(() =>
            decoder.DecodeSamples(null!, []));
        Assert.Throws<ArgumentNullException>(() =>
            decoder.DecodeSamples(track, null!));
    }

    private static Mp4MediaInfo EmptyMediaInfo() =>
        new(new Mp4MovieHeader(1000, 0), [], [], []);

    [Fact]
    public void DecodeTargetsFromSample_ShouldRejectNullInputs()
    {
        H264Mp4TrackDecoder decoder = new();
        using MemoryStream stream = new();
        Mp4MediaInfo mediaInfo = EmptyMediaInfo();
        long[] targets = [0];

        Assert.Throws<ArgumentNullException>(() =>
            decoder.DecodeTargetsFromSample(null!, mediaInfo, 1, 1, targets));
        Assert.Throws<ArgumentNullException>(() =>
            decoder.DecodeTargetsFromSample(stream, null!, 1, 1, targets));
        Assert.Throws<ArgumentNullException>(() =>
            decoder.DecodeTargetsFromSample(stream, mediaInfo, 1, 1, null!));
    }

    [Fact]
    public void DecodeTargetsFromSample_ShouldRejectDescendingTargets()
    {
        H264Mp4TrackDecoder decoder = new();
        using MemoryStream stream = new();

        Assert.Throws<ArgumentException>(() =>
            decoder.DecodeTargetsFromSample(
                stream, EmptyMediaInfo(), 1, 1, new long[] { 10, 5 }));
    }

    [Fact]
    public void DecodeTargetsFromSample_ShouldRejectUnknownTrack()
    {
        H264Mp4TrackDecoder decoder = new();
        using MemoryStream stream = new();

        Assert.Throws<ArgumentException>(() =>
            decoder.DecodeTargetsFromSample(
                stream, EmptyMediaInfo(), 1, 1, new long[] { 0 }));
    }

    [Fact]
    public void DecodeSyncSample_ShouldRejectNullInputsAndUnknownTrack()
    {
        H264Mp4TrackDecoder decoder = new();
        using MemoryStream stream = new();
        Mp4MediaInfo mediaInfo = EmptyMediaInfo();

        Assert.Throws<ArgumentNullException>(() =>
            decoder.DecodeSyncSample(null!, mediaInfo, 1, 1));
        Assert.Throws<ArgumentNullException>(() =>
            decoder.DecodeSyncSample(stream, null!, 1, 1));
        Assert.Throws<ArgumentException>(() =>
            decoder.DecodeSyncSample(stream, mediaInfo, 1, 1));
    }
}

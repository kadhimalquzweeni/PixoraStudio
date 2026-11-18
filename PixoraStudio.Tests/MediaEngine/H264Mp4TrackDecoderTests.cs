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
}

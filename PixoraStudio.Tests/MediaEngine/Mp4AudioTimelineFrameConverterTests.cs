using PixoraStudio.MediaEngine.Processing.Timeline;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4AudioTimelineFrameConverterTests
{
    [Fact]
    public void ToSampleFrame_ShouldConvertTrackTicksToPcmFrames()
    {
        Assert.Equal(
            44100,
            Mp4AudioTimelineFrameConverter.ToSampleFrame(
                presentationTimeTicks: 1000,
                trackTimescale: 1000,
                sampleRate: 44100));
    }

    [Fact]
    public void ToSampleFrame_ShouldPreserveNegativePreroll()
    {
        Assert.Equal(
            -512,
            Mp4AudioTimelineFrameConverter.ToSampleFrame(
                presentationTimeTicks: -512,
                trackTimescale: 44100,
                sampleRate: 44100));
    }

    [Fact]
    public void ToSampleFrame_ShouldRoundHalfFramesAwayFromZero()
    {
        Assert.Equal(
            1,
            Mp4AudioTimelineFrameConverter.ToSampleFrame(1, 2, 1));
        Assert.Equal(
            -1,
            Mp4AudioTimelineFrameConverter.ToSampleFrame(-1, 2, 1));
    }

    [Fact]
    public void ToSampleFrame_ShouldConvertEachAbsoluteTimestampWithoutAccumulation()
    {
        long first = Mp4AudioTimelineFrameConverter.ToSampleFrame(1024, 44100, 48000);
        long second = Mp4AudioTimelineFrameConverter.ToSampleFrame(2048, 44100, 48000);
        long third = Mp4AudioTimelineFrameConverter.ToSampleFrame(3072, 44100, 48000);

        Assert.Equal(1115, first);
        Assert.Equal(2229, second);
        Assert.Equal(3344, third);
    }

    [Theory]
    [InlineData(0, 44100)]
    [InlineData(1000, 0)]
    [InlineData(1000, -1)]
    public void ToSampleFrame_ShouldRejectInvalidClockParameters(
        uint timescale,
        int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Mp4AudioTimelineFrameConverter.ToSampleFrame(0, timescale, sampleRate));
    }

    [Fact]
    public void ToSampleFrame_ShouldRejectFramePositionOverflow()
    {
        Assert.Throws<OverflowException>(() =>
            Mp4AudioTimelineFrameConverter.ToSampleFrame(
                long.MaxValue,
                1,
                int.MaxValue));
    }
}

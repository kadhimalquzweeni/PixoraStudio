using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4FramePresentationGateTests
{
    [Fact]
    public void TryAcceptFrame_WithoutRequestedSample_ShouldAcceptEveryFrame()
    {
        H264Mp4FramePresentationGate gate = new();

        Assert.True(gate.TryAcceptFrame(Frame(1, 0), out bool firstRequested));
        Assert.False(firstRequested);
        Assert.True(gate.TryAcceptFrame(Frame(2, 40), out bool secondRequested));
        Assert.False(secondRequested);
        Assert.True(gate.HasReachedRequestedFrame);
    }

    [Fact]
    public void TryAcceptFrame_ShouldSkipFramesUntilRequestedSample()
    {
        H264Mp4FramePresentationGate gate = new(requestedSampleNumber: 3);

        Assert.False(gate.TryAcceptFrame(Frame(1, 0), out _));
        Assert.False(gate.TryAcceptFrame(Frame(2, 40), out _));
        Assert.False(gate.HasReachedRequestedFrame);

        Assert.True(gate.TryAcceptFrame(Frame(3, 80), out bool isRequested));
        Assert.True(isRequested);
        Assert.True(gate.HasReachedRequestedFrame);
    }

    [Fact]
    public void TryAcceptFrame_ShouldForceOnlyTheRequestedFrame()
    {
        H264Mp4FramePresentationGate gate = new(requestedSampleNumber: 2);

        Assert.False(gate.TryAcceptFrame(Frame(1, 0), out bool before));
        Assert.False(before);

        Assert.True(gate.TryAcceptFrame(Frame(2, 40), out bool requested));
        Assert.True(requested);

        Assert.True(gate.TryAcceptFrame(Frame(3, 80), out bool after));
        Assert.False(after);
    }

    [Fact]
    public void ShouldWaitForFrame_DuplicatePts_ShouldHoldUntilPlaybackClockAdvances()
    {
        H264Mp4FramePresentationGate gate = new(requestedSampleNumber: 2);
        Assert.True(gate.TryAcceptFrame(Frame(2, 40), out _));

        Assert.True(gate.ShouldWaitForFrame(Frame(3, 40), 0.04));
        Assert.True(gate.ShouldWaitForFrame(Frame(4, 44), 0.04));
        Assert.False(gate.ShouldWaitForFrame(Frame(3, 40), 0.046));
    }

    [Fact]
    public void ShouldWaitForFrame_DistinctLaterPts_ShouldNotHoldTheNextFrame()
    {
        H264Mp4FramePresentationGate gate = new(requestedSampleNumber: 2);
        Assert.True(gate.TryAcceptFrame(Frame(2, 40), out _));

        Assert.False(gate.ShouldWaitForFrame(Frame(3, 80), 0.04));
    }

    [Fact]
    public void HasReachedRequestedFrame_ShouldRemainFalseWhenTargetIsMissing()
    {
        H264Mp4FramePresentationGate gate = new(requestedSampleNumber: 4);

        Assert.True(gate.TryAcceptFrame(Frame(1, 0), out _));
        Assert.True(gate.TryAcceptFrame(Frame(2, 40), out _));
        Assert.False(gate.HasReachedRequestedFrame);
    }

    [Fact]
    public void Constructor_ShouldRejectSampleNumberZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new H264Mp4FramePresentationGate(0));
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ShouldWaitForFrame_ShouldRejectInvalidPlaybackTime(double playbackTime)
    {
        H264Mp4FramePresentationGate gate = new(requestedSampleNumber: 1);
        Assert.True(gate.TryAcceptFrame(Frame(1, 0), out _));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => gate.ShouldWaitForFrame(Frame(2, 0), playbackTime));
    }

    private static H264Mp4DecodedVideoFrame Frame(
        uint sampleNumber,
        long presentationTimeTicks)
    {
        H264Mp4VideoSample sample = new(
            1,
            sampleNumber,
            checked((ulong)Math.Max(0, presentationTimeTicks)),
            presentationTimeTicks,
            40,
            1000,
            true,
            null!,
            []);

        return new H264Mp4DecodedVideoFrame(null!, 0, sample);
    }
}

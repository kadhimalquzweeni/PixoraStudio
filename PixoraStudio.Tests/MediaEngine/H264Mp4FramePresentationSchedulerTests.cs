using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4FramePresentationSchedulerTests
{
    private readonly H264Mp4FramePresentationScheduler _scheduler = new();

    [Fact]
    public void Evaluate_FrameAheadOfClock_ShouldWaitForBoundedInterval()
    {
        H264FramePresentationDecision result = _scheduler.Evaluate(2.0, 1.9);

        Assert.Equal(H264FramePresentationAction.Wait, result.Action);
        Assert.Equal(TimeSpan.FromMilliseconds(25), result.Delay);
    }

    [Fact]
    public void Evaluate_FrameWithinEarlyTolerance_ShouldPresent()
    {
        H264FramePresentationDecision result = _scheduler.Evaluate(1.904, 1.9);

        Assert.Equal(H264FramePresentationAction.Present, result.Action);
        Assert.Equal(TimeSpan.Zero, result.Delay);
    }

    [Fact]
    public void Evaluate_FrameDueAtClock_ShouldPresent()
    {
        H264FramePresentationDecision result = _scheduler.Evaluate(1.9, 1.9);

        Assert.Equal(H264FramePresentationAction.Present, result.Action);
    }

    [Fact]
    public void Evaluate_FrameTooLate_ShouldDropIt()
    {
        H264FramePresentationDecision result = _scheduler.Evaluate(1.0, 1.2);

        Assert.Equal(H264FramePresentationAction.Drop, result.Action);
    }

    [Fact]
    public void Evaluate_FrameAtMaximumLatenessBoundary_ShouldPresent()
    {
        H264FramePresentationDecision result = _scheduler.Evaluate(1.12, 1.2, 0.08);

        Assert.Equal(H264FramePresentationAction.Present, result.Action);
    }

    [Fact]
    public void GetMaximumLatenessSeconds_LowFrameRate_ShouldAllowTwoFrameDurations()
    {
        Assert.Equal(
            2.0 / 24.0,
            H264Mp4FramePresentationScheduler.GetMaximumLatenessSeconds(1.0 / 24.0),
            8);
    }

    [Fact]
    public void GetMaximumLatenessSeconds_HighFrameRate_ShouldKeepMinimumJitterAllowance()
    {
        Assert.Equal(
            0.020,
            H264Mp4FramePresentationScheduler.GetMaximumLatenessSeconds(1.0 / 120.0),
            8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GetMaximumLatenessSeconds_InvalidFrameDuration_ShouldThrow(
        double frameDuration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264Mp4FramePresentationScheduler.GetMaximumLatenessSeconds(frameDuration));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(0, -0.1)]
    public void Evaluate_InvalidTimes_ShouldThrow(
        double frameTime,
        double playbackTime)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _scheduler.Evaluate(frameTime, playbackTime));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Evaluate_InvalidMaximumLateness_ShouldThrow(double maximumLateness)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _scheduler.Evaluate(0, 0, maximumLateness));
    }
}

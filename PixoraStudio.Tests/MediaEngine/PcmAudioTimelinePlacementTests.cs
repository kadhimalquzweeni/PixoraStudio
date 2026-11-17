using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class PcmAudioTimelinePlacementTests
{
    [Fact]
    public void Calculate_ShouldPlaceContiguousBlockWithoutSilenceOrSkipping()
    {
        PcmAudioTimelinePlacementResult result =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: 100,
                writtenFrames: 100,
                decodedFrameCount: 1024);

        Assert.Equal(0, result.SilenceFrames);
        Assert.Equal(0, result.FramesToSkip);
        Assert.Equal(1024, result.FramesToWrite);
        Assert.Equal(1124, result.ResultingWrittenFrames);
    }

    [Fact]
    public void Calculate_ShouldInsertSilenceForPresentationGap()
    {
        PcmAudioTimelinePlacementResult result =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: 1500,
                writtenFrames: 1000,
                decodedFrameCount: 1024);

        Assert.Equal(500, result.SilenceFrames);
        Assert.Equal(0, result.FramesToSkip);
        Assert.Equal(1024, result.FramesToWrite);
        Assert.Equal(2524, result.ResultingWrittenFrames);
    }

    [Fact]
    public void Calculate_ShouldSkipSamplesAlreadyCoveredByPreviousBlock()
    {
        PcmAudioTimelinePlacementResult result =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: 900,
                writtenFrames: 1000,
                decodedFrameCount: 1024);

        Assert.Equal(0, result.SilenceFrames);
        Assert.Equal(100, result.FramesToSkip);
        Assert.Equal(924, result.FramesToWrite);
        Assert.Equal(1924, result.ResultingWrittenFrames);
    }

    [Fact]
    public void Calculate_ShouldSkipNegativePresentationPreroll()
    {
        PcmAudioTimelinePlacementResult result =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: -200,
                writtenFrames: 0,
                decodedFrameCount: 1024);

        Assert.Equal(0, result.SilenceFrames);
        Assert.Equal(200, result.FramesToSkip);
        Assert.Equal(824, result.FramesToWrite);
        Assert.Equal(824, result.ResultingWrittenFrames);
    }

    [Fact]
    public void Calculate_ShouldSkipEntireBlockWhenItIsAlreadyPresented()
    {
        PcmAudioTimelinePlacementResult result =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: 100,
                writtenFrames: 1200,
                decodedFrameCount: 1024);

        Assert.Equal(0, result.SilenceFrames);
        Assert.Equal(1024, result.FramesToSkip);
        Assert.Equal(0, result.FramesToWrite);
        Assert.Equal(1200, result.ResultingWrittenFrames);
    }

    [Fact]
    public void Calculate_ShouldHandleBlockEntirelyBeforePresentationStart()
    {
        PcmAudioTimelinePlacementResult result =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: -2000,
                writtenFrames: 0,
                decodedFrameCount: 1024);

        Assert.Equal(0, result.SilenceFrames);
        Assert.Equal(1024, result.FramesToSkip);
        Assert.Equal(0, result.FramesToWrite);
        Assert.Equal(0, result.ResultingWrittenFrames);
    }

    [Fact]
    public void Calculate_ShouldHandleLargeExistingTimelineWithoutOverlapOverflow()
    {
        PcmAudioTimelinePlacementResult result =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: -1,
                writtenFrames: long.MaxValue,
                decodedFrameCount: 1024);

        Assert.Equal(0, result.SilenceFrames);
        Assert.Equal(1024, result.FramesToSkip);
        Assert.Equal(0, result.FramesToWrite);
        Assert.Equal(long.MaxValue, result.ResultingWrittenFrames);
    }

    [Fact]
    public void Calculate_ShouldRejectTimelineLengthOverflow()
    {
        Assert.Throws<OverflowException>(
            () => PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame: long.MaxValue,
                writtenFrames: 0,
                decodedFrameCount: 1));
    }

    [Theory]
    [InlineData(0, -1, 1024)]
    [InlineData(0, 0, -1)]
    [InlineData(long.MinValue, 0, 1024)]
    public void Calculate_ShouldRejectInvalidArguments(
        long presentationStartFrame,
        long writtenFrames,
        int decodedFrameCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame,
                writtenFrames,
                decodedFrameCount));
    }
}

using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4SampleLocationTests
{
    [Fact]
    public void PresentationTimeTicks_ShouldEqualDecodeTimeWhenOffsetIsZero()
    {
        Mp4SampleLocation sample = CreateSample(100, 0);

        Assert.Equal(100L, sample.PresentationTimeTicks);
    }

    [Fact]
    public void PresentationTimeTicks_ShouldAddPositiveCompositionOffset()
    {
        Mp4SampleLocation sample = CreateSample(100, 50);

        Assert.Equal(150L, sample.PresentationTimeTicks);
    }

    [Fact]
    public void PresentationTimeTicks_ShouldSubtractNegativeCompositionOffset()
    {
        Mp4SampleLocation sample = CreateSample(100, -30);

        Assert.Equal(70L, sample.PresentationTimeTicks);
    }

    [Fact]
    public void PresentationTimeTicks_ShouldSupportZeroTimestamp()
    {
        Mp4SampleLocation sample = CreateSample(0, 0);

        Assert.Equal(0L, sample.PresentationTimeTicks);
    }

    [Fact]
    public void PresentationTimeTicks_ShouldRejectNegativeResult()
    {
        Mp4SampleLocation sample = CreateSample(10, -20);

        Assert.Equal(-10L, sample.PresentationTimeTicks);
    }

    [Fact]
    public void PresentationTimeTicks_ShouldRejectDecodeTimestampAboveLongMaxValue()
    {
        Mp4SampleLocation sample = CreateSample(
            (ulong)long.MaxValue + 1,
            0);

        Assert.Throws<OverflowException>(
            () => _ = sample.PresentationTimeTicks);
    }

    [Fact]
    public void PresentationTimeTicks_ShouldRejectPositiveOverflow()
    {
        Mp4SampleLocation sample = CreateSample(
            (ulong)long.MaxValue,
            1);

        Assert.Throws<OverflowException>(
            () => _ = sample.PresentationTimeTicks);
    }

    [Fact]
    public void PresentationTimeTicks_ShouldRejectNegativeUnderflow()
    {
        Mp4SampleLocation sample = CreateSample(
            0,
            int.MinValue);

        Assert.Equal((long)int.MinValue, sample.PresentationTimeTicks);
    }

    private static Mp4SampleLocation CreateSample(
        ulong decodeTime,
        int compositionOffset)
    {
        return new Mp4SampleLocation(
            1,
            1,
            1000,
            100,
            decodeTime,
            100,
            true)
        {
            CompositionOffsetTicks = compositionOffset
        };
    }
}

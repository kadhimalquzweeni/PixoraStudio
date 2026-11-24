using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacMidSideStereoProcessorTests
{
    [Fact]
    public void Apply_ShouldReconstructMidSideForSelectedLines()
    {
        float[] left = [3f, 4f, 5f];
        float[] right = [1f, 2f, 3f];
        bool[] mask = [true, false, true];

        new AacMidSideStereoProcessor().Apply(left, right, mask);

        Assert.Equal(new[] { 4f, 4f, 8f }, left);
        Assert.Equal(new[] { 2f, 2f, 2f }, right);
    }

    [Fact]
    public void Apply_ShouldLeaveUnselectedLinesUnchanged()
    {
        float[] left = [0.25f, -0.5f];
        float[] right = [0.75f, 0.5f];

        new AacMidSideStereoProcessor().Apply(left, right, new bool[2]);

        Assert.Equal(new[] { 0.25f, -0.5f }, left);
        Assert.Equal(new[] { 0.75f, 0.5f }, right);
    }

    [Fact]
    public void Apply_ShouldRejectMismatchedSpectrumLengths()
    {
        Assert.Throws<ArgumentException>(() =>
            new AacMidSideStereoProcessor().Apply(
                new float[2], new float[1], new bool[2]));
    }

    [Fact]
    public void Apply_ShouldRejectMismatchedMaskLength()
    {
        Assert.Throws<ArgumentException>(() =>
            new AacMidSideStereoProcessor().Apply(
                new float[2], new float[2], new bool[1]));
    }
}

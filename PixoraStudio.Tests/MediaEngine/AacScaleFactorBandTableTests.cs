using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacScaleFactorBandTableTests
{
    [Theory]
    [InlineData(0, 41, 12)]
    [InlineData(1, 41, 12)]
    [InlineData(2, 47, 12)]
    [InlineData(3, 49, 14)]
    [InlineData(4, 49, 14)]
    [InlineData(5, 51, 14)]
    [InlineData(6, 47, 15)]
    [InlineData(7, 47, 15)]
    [InlineData(8, 43, 15)]
    [InlineData(9, 43, 15)]
    [InlineData(10, 43, 15)]
    [InlineData(11, 40, 15)]
    [InlineData(12, 40, 15)]
    public void GetOffsets_ShouldReturnExpectedBandCounts(
        int index, int longBands, int shortBands)
    {
        var longOffsets = AacScaleFactorBandTable.GetLongWindowOffsets(index);
        var shortOffsets = AacScaleFactorBandTable.GetShortWindowOffsets(index);

        Assert.Equal(longBands + 1, longOffsets.Count);
        Assert.Equal(shortBands + 1, shortOffsets.Count);
        Assert.Equal(0, longOffsets[0]);
        Assert.Equal(1024, longOffsets[^1]);
        Assert.Equal(0, shortOffsets[0]);
        Assert.Equal(128, shortOffsets[^1]);
        Assert.All(longOffsets.Zip(longOffsets.Skip(1)), pair =>
            Assert.True(pair.Second > pair.First));
        Assert.All(shortOffsets.Zip(shortOffsets.Skip(1)), pair =>
            Assert.True(pair.Second > pair.First));
    }

    [Fact]
    public void GetOffsets_ShouldReturnDefensiveCopies()
    {
        var first = AacScaleFactorBandTable.GetLongWindowOffsets(0);
        var second = AacScaleFactorBandTable.GetLongWindowOffsets(0);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void GetOffsets_ShouldRejectInvalidSamplingFrequencyIndex(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AacScaleFactorBandTable.GetLongWindowOffsets(index));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AacScaleFactorBandTable.GetShortWindowOffsets(index));
    }
}

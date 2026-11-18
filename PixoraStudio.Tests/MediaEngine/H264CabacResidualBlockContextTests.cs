using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacResidualBlockContextTests
{
    [Theory]
    [InlineData(0, 16, 105, 166, 227)]
    [InlineData(1, 15, 120, 181, 237)]
    [InlineData(2, 16, 134, 195, 247)]
    [InlineData(3, 4, 149, 210, 257)]
    [InlineData(4, 15, 152, 213, 266)]
    [InlineData(5, 64, 402, 417, 426)]
    public void Create_ShouldReturnNormativeCategoryOffsets(
        int category, int maximum, int significant, int last, int level)
    {
        H264CabacResidualBlockContextMap map =
            H264CabacResidualBlockContext.Create(category);

        Assert.Equal(maximum, map.MaximumCoefficients);
        Assert.Equal(significant, map.SignificantContextOffset);
        Assert.Equal(last, map.LastSignificantContextOffset);
        Assert.Equal(level, map.AbsoluteLevelContextOffset);
    }

    [Theory]
    [InlineData(0, 16)]
    [InlineData(1, 15)]
    [InlineData(2, 16)]
    [InlineData(3, 4)]
    [InlineData(4, 15)]
    public void Create_ShouldUseSequentialScanPositionIncrements(
        int category, int expectedCount)
    {
        H264CabacResidualBlockContextMap map =
            H264CabacResidualBlockContext.Create(category);

        Assert.Equal(expectedCount - 1, map.SignificantContextIncrements.Count);
        Assert.Equal(Enumerable.Range(0, expectedCount - 1),
            map.SignificantContextIncrements);
        Assert.Equal(map.SignificantContextIncrements,
            map.LastSignificantContextIncrements);
        Assert.False(map.HasImplicitCodedBlockFlag);
    }

    [Fact]
    public void Create_ShouldUseSpecialLuma8X8PositionMaps()
    {
        H264CabacResidualBlockContextMap map =
            H264CabacResidualBlockContext.Create(5);

        Assert.Equal(63, map.SignificantContextIncrements.Count);
        Assert.Equal(63, map.LastSignificantContextIncrements.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 5, 4, 4, 3, 3, 4, 4, 4, 5, 5 },
            map.SignificantContextIncrements.Take(16));
        Assert.Equal(new[] { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
            map.LastSignificantContextIncrements.Take(16));
        Assert.Equal(14, map.SignificantContextIncrements.Max());
        Assert.Equal(8, map.LastSignificantContextIncrements.Max());
        Assert.True(map.HasImplicitCodedBlockFlag);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(13)]
    public void Create_ShouldRejectUnsupportedResidualCategories(int category)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264CabacResidualBlockContext.Create(category));
    }
}

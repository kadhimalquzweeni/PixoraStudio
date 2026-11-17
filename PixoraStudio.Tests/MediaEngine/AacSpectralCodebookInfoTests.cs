using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacSpectralCodebookInfoTests
{
    [Theory]
    [InlineData(1, 4, 1, true, false)]
    [InlineData(2, 4, 1, true, false)]
    [InlineData(3, 4, 2, false, false)]
    [InlineData(4, 4, 2, false, false)]
    [InlineData(5, 2, 4, true, false)]
    [InlineData(6, 2, 4, true, false)]
    [InlineData(7, 2, 7, false, false)]
    [InlineData(8, 2, 7, false, false)]
    [InlineData(9, 2, 12, false, false)]
    [InlineData(10, 2, 12, false, false)]
    [InlineData(11, 2, 16, false, true)]
    public void Get_ShouldReturnCorrectCodebookProperties(
        int index,
        int tupleSize,
        int maximumMagnitude,
        bool isSigned,
        bool usesEscapeValues)
    {
        AacSpectralCodebookInfo result =
            AacSpectralCodebookInfo.Get(index);

        Assert.Equal(index, result.Index);
        Assert.Equal(tupleSize, result.TupleSize);
        Assert.Equal(maximumMagnitude, result.MaximumMagnitude);
        Assert.Equal(isSigned, result.IsSigned);
        Assert.Equal(usesEscapeValues, result.UsesEscapeValues);
    }

    [Fact]
    public void Get_ShouldRejectInvalidIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AacSpectralCodebookInfo.Get(12));
    }

    [Fact]
    public void Get_ShouldSupportZeroCodebook()
    {
        AacSpectralCodebookInfo result =
            AacSpectralCodebookInfo.Get(0);

        Assert.Equal(0, result.TupleSize);
        Assert.Equal(0, result.MaximumMagnitude);
    }
}
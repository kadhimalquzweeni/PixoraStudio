using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class VolumeIconSelectorTests
{
    [Theory]
    [InlineData(1d, "VolumeIconGeometry")]
    [InlineData(19.99d, "VolumeIconGeometry")]
    [InlineData(20d, "Volume1IconGeometry")]
    [InlineData(59.99d, "Volume1IconGeometry")]
    [InlineData(60d, "Volume2IconGeometry")]
    [InlineData(100d, "Volume2IconGeometry")]
    public void GetResourceKey_ShouldSelectIconForVolumeLevel(
        double volumePercent,
        string expectedResourceKey)
    {
        string actual = VolumeIconSelector.GetResourceKey(
            volumePercent,
            isMuted: false);

        Assert.Equal(expectedResourceKey, actual);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(35d)]
    [InlineData(100d)]
    public void GetResourceKey_ShouldPreferMutedIconRegardlessOfVolume(
        double volumePercent)
    {
        string actual = VolumeIconSelector.GetResourceKey(
            volumePercent,
            isMuted: true);

        Assert.Equal("VolumeXIconGeometry", actual);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-10d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GetResourceKey_ShouldUseVolumeOffIconWhenVolumeIsZeroOrInvalid(
        double volumePercent)
    {
        string actual = VolumeIconSelector.GetResourceKey(
            volumePercent,
            isMuted: false);

        Assert.Equal("VolumeOffIconGeometry", actual);
    }
}

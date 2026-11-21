using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264SliceParallelismConfigurationTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("4", 4)]
    [InlineData("8", 4)]
    [InlineData("100", 4)]
    public void ResolveMaxSliceParallelism_ShouldClampPositiveConfiguredValues(
        string configured,
        int expected)
    {
        int actual = H264Mp4AccessUnitDecoder.ResolveMaxSliceParallelism(
            configured, processorCount: 8);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    public void ResolveMaxSliceParallelism_ShouldUseProcessorBasedDefaultForInvalidValues(
        string? configured)
    {
        int actual = H264Mp4AccessUnitDecoder.ResolveMaxSliceParallelism(
            configured, processorCount: 8);

        Assert.Equal(2, actual);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 2)]
    public void ResolveMaxSliceParallelism_ShouldLimitDefaultNestedParallelism(
        int processorCount,
        int expected)
    {
        int actual = H264Mp4AccessUnitDecoder.ResolveMaxSliceParallelism(
            configured: null, processorCount);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveMaxSliceParallelism_ShouldRejectNonPositiveProcessorCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264Mp4AccessUnitDecoder.ResolveMaxSliceParallelism(
                configured: null, processorCount: 0));
    }
}

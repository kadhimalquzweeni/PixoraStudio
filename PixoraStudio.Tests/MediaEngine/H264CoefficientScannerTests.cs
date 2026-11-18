using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CoefficientScannerTests
{
    [Fact]
    public void ToRasterOrder_ShouldApplyZigZagScan()
    {
        int[] scan = Enumerable.Range(0, 16).ToArray();
        int[] raster = H264CoefficientScanner.ToRasterOrder(scan);
        Assert.Equal(new[] { 0, 5, 1, 2, 3, 4, 6, 10, 7, 8, 9, 13, 11, 12, 14, 15 }, raster);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanConversions_ShouldRoundTrip(bool fieldScan)
    {
        int[] raster = Enumerable.Range(-8, 16).ToArray();
        int[] scan = H264CoefficientScanner.ToScanOrder(raster, fieldScan);
        Assert.Equal(raster, H264CoefficientScanner.ToRasterOrder(scan, fieldScan));
    }

    [Fact]
    public void ToRasterOrder_ShouldRejectWrongLength()
    {
        Assert.Throws<ArgumentException>(() => H264CoefficientScanner.ToRasterOrder(new int[15]));
    }
}

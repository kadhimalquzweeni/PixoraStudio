using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CoefficientScannerTests
{
    [Fact]
    public void ToRasterOrder_ShouldApplyZigZagScan()
    {
        int[] scan = Enumerable.Range(0, 16).ToArray();

        int[] raster = H264CoefficientScanner.ToRasterOrder(scan);

        Assert.Equal(
            new[] { 0, 1, 5, 6, 2, 4, 7, 12, 3, 8, 11, 13, 9, 10, 14, 15 },
            raster);
    }

    [Fact]
    public void ToScanOrder_ShouldApplyZigZagScan()
    {
        int[] raster = Enumerable.Range(0, 16).ToArray();

        int[] scan = H264CoefficientScanner.ToScanOrder(raster);

        Assert.Equal(
            new[] { 0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15 },
            scan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanConversions_ShouldRoundTrip(bool fieldScan)
    {
        int[] raster = Enumerable.Range(-8, 16).ToArray();

        int[] scan = H264CoefficientScanner.ToScanOrder(raster, fieldScan);
        int[] reconstructed = H264CoefficientScanner.ToRasterOrder(scan, fieldScan);

        Assert.Equal(raster, reconstructed);
    }

    [Fact]
    public void ToRasterOrder_ShouldRejectWrongLength()
    {
        Assert.Throws<ArgumentException>(() => H264CoefficientScanner.ToRasterOrder(new int[15]));
    }

    [Fact]
    public void ToScanOrder_ShouldRejectWrongLength()
    {
        Assert.Throws<ArgumentException>(() => H264CoefficientScanner.ToScanOrder(new int[15]));
    }

    [Fact]
    public void ToScanOrder8x8_ShouldStartWithNormativeZigZagPositions()
    {
        int[] raster = Enumerable.Range(0, 64).ToArray();

        Assert.Equal(
            new[] { 0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5 },
            H264CoefficientScanner.ToScanOrder(raster).Take(16));
    }

    [Fact]
    public void ToScanOrder8x8_ShouldStartWithNormativeFieldPositions()
    {
        int[] raster = Enumerable.Range(0, 64).ToArray();

        Assert.Equal(
            new[] { 0, 8, 16, 1, 9, 24, 32, 17, 2, 25, 40, 48, 56, 33, 10, 3 },
            H264CoefficientScanner.ToScanOrder(raster, fieldScan: true).Take(16));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScanConversions8x8_ShouldRoundTrip(bool fieldScan)
    {
        int[] raster = Enumerable.Range(-32, 64).ToArray();

        int[] scan = H264CoefficientScanner.ToScanOrder(raster, fieldScan);
        int[] reconstructed = H264CoefficientScanner.ToRasterOrder(scan, fieldScan);

        Assert.Equal(raster, reconstructed);
    }

    [Fact]
    public void ToRasterOrder8x8_ShouldRejectWrongLength()
    {
        Assert.Throws<ArgumentException>(() => H264CoefficientScanner.ToRasterOrder(new int[63]));
    }

    [Fact]
    public void ToScanOrder8x8_ShouldRejectWrongLength()
    {
        Assert.Throws<ArgumentException>(() => H264CoefficientScanner.ToScanOrder(new int[63]));
    }
}

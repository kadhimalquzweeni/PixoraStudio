using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InverseQuantizerTests
{
    private static readonly int[] FlatScalingList = Enumerable.Repeat(16, 16).ToArray();

    [Fact]
    public void Dequantize4x4_ShouldApplyThreePositionClassesAtQpZero()
    {
        int[] result = H264InverseQuantizer.Dequantize4x4(
            Enumerable.Repeat(1, 16).ToArray(), 0, FlatScalingList);
        Assert.Equal(10, result[0]);
        Assert.Equal(16, result[1]);
        Assert.Equal(13, result[5]);
        Assert.Equal(10, result[10]);
    }

    [Fact]
    public void Dequantize4x4_ShouldApplyQpRemainderAndQuotient()
    {
        int[] coefficients = new int[16];
        coefficients[0] = 1;
        Assert.Equal(16, H264InverseQuantizer.Dequantize4x4(coefficients, 6, FlatScalingList)[0]);
        Assert.Equal(256, H264InverseQuantizer.Dequantize4x4(coefficients, 24, FlatScalingList)[0]);
        Assert.Equal(576, H264InverseQuantizer.Dequantize4x4(coefficients, 30, FlatScalingList)[0]);
    }

    [Fact]
    public void Dequantize4x4_ShouldApplyCustomScalingList()
    {
        int[] coefficients = new int[16];
        coefficients[0] = 1;
        Assert.Equal(5, H264InverseQuantizer.Dequantize4x4(
            coefficients, 0, Enumerable.Repeat(8, 16).ToArray())[0]);
    }

    [Fact]
    public void Dequantize4x4_ShouldPreserveCoefficientSign()
    {
        int[] coefficients = new int[16];
        coefficients[0] = -3;
        Assert.Equal(-30, H264InverseQuantizer.Dequantize4x4(coefficients, 0, FlatScalingList)[0]);
    }

    [Fact]
    public void CalculateLumaQpY_ShouldCombinePpsInitialQpAndSliceDelta()
    {
        int qpY = H264InverseQuantizer.CalculateLumaQpY(
            CreateSps(), CreatePps() with { PicInitQpMinus26 = -2 },
            CreateSlice() with { SliceQpDelta = 3 });
        Assert.Equal(27, qpY);
    }

    [Fact]
    public void CalculateLumaQpY_ShouldAllowNegativeQpForHighBitDepth()
    {
        H264SequenceParameterSet sps = CreateSps() with { BitDepthLumaMinus8 = 2 };
        H264PictureParameterSetHeader pps = CreatePps() with { PicInitQpMinus26 = -26 };
        H264SliceHeader slice = CreateSlice() with { SliceQpDelta = -5 };
        int qpY = H264InverseQuantizer.CalculateLumaQpY(sps, pps, slice);
        Assert.Equal(-5, qpY);
        Assert.Equal(7, H264InverseQuantizer.ToQpPrimeY(sps, qpY));
    }

    [Fact]
    public void CalculateLumaQpY_ShouldRejectQpOutsideBitDepthRange()
    {
        H264PictureParameterSetHeader pps = CreatePps() with { PicInitQpMinus26 = 25 };
        Assert.Throws<FormatException>(() =>
            H264InverseQuantizer.CalculateLumaQpY(CreateSps(), pps, CreateSlice()));
    }

    [Fact]
    public void Dequantize4x4_ShouldRejectInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() =>
            H264InverseQuantizer.Dequantize4x4(new int[15], 0, FlatScalingList));
        Assert.Throws<ArgumentException>(() =>
            H264InverseQuantizer.Dequantize4x4(new int[16], 0, new int[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264InverseQuantizer.Dequantize4x4(new int[16], 88, FlatScalingList));

        int[] invalidScalingList = Enumerable.Repeat(16, 16).ToArray();
        invalidScalingList[4] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264InverseQuantizer.Dequantize4x4(new int[16], 0, invalidScalingList));
    }

    private static H264SequenceParameterSet CreateSps() => new(
        0, 66, 30, 1, false, 0, 0, 0, 0, 0, 2,
        39, 22, true, true, 640, 368, false);

    private static H264PictureParameterSetHeader CreatePps() => new(0, 0, false, false, 0);

    private static H264SliceHeader CreateSlice() => new(
        0, H264SliceType.I, 0, 0, 0, false, false, null, null,
        null, null, null, null, false, false, 0, 0, [], [], null,
        null, null, 0, false, null, null, null, null, null);
}

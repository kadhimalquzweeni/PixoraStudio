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
        // With a flat scaling list of 16, the even/even level scale is 10.
        // QP 6 uses one right-shift stage; QP 24 has no shift; QP 30 shifts once left.
        Assert.Equal(20, H264InverseQuantizer.Dequantize4x4(coefficients, 6, FlatScalingList)[0]);
        Assert.Equal(160, H264InverseQuantizer.Dequantize4x4(coefficients, 24, FlatScalingList)[0]);
        Assert.Equal(320, H264InverseQuantizer.Dequantize4x4(coefficients, 30, FlatScalingList)[0]);
    }

    [Fact]
    public void Dequantize4x4_ShouldResolveScalingListFromSpsAndPps()
    {
        H264SequenceParameterSet sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true,
            ScalingLists = Enumerable.Range(0, 8)
                .Select(_ => (IReadOnlyList<int>)Array.Empty<int>())
                .ToArray(),
            ScalingListUseDefault = new bool[8]
        };
        H264PictureParameterSetHeader pps = CreatePps();

        int[] coefficients = new int[16];
        coefficients[0] = 1;

        // With the SPS scaling matrix present but list 0 absent, H.264 selects
        // the default 4x4 intra matrix whose first raster entry is 6.
        int[] result = H264InverseQuantizer.Dequantize4x4(
            coefficients, 0, sps, pps);

        Assert.Equal(4, result[0]);
    }

    [Fact]
    public void Dequantize4x4_ShouldRejectInvalidResolvedScalingListIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264InverseQuantizer.Dequantize4x4(
                new int[16], 0, CreateSps(), CreatePps(), 6));
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
    public void Dequantize8x8_ShouldApplySixPositionClassesAndQpScaling()
    {
        int[] coefficients = new int[64];
        coefficients[0] = coefficients[1] = coefficients[2] = coefficients[9] = 1;
        int[] flat = Enumerable.Repeat(16, 64).ToArray();
        int[] result = H264InverseQuantizer.Dequantize8x8(coefficients, 0, flat);
        Assert.Equal(5, result[0]);
        Assert.Equal(7, result[1]);
        Assert.Equal(8, result[2]);
        Assert.Equal(6, result[9]);
        Assert.Equal(10, H264InverseQuantizer.Dequantize8x8(coefficients, 6, flat)[0]);
        Assert.Equal(320, H264InverseQuantizer.Dequantize8x8(coefficients, 36, flat)[0]);
    }

    [Fact]
    public void Dequantize8x8_ShouldResolveDefaultIntraMatrixFromParameterSets()
    {
        var sps = CreateSps() with
        {
            ScalingMatrixPresentFlag = true,
            ScalingLists = Enumerable.Range(0, 8).Select(_ => (IReadOnlyList<int>)Array.Empty<int>()).ToArray(),
            ScalingListUseDefault = new bool[8]
        };
        var pps = CreatePps() with { Transform8X8ModeFlag = true };
        int[] coefficients = new int[64];
        coefficients[0] = 1;
        int[] result = H264InverseQuantizer.Dequantize8x8(coefficients, 0, sps, pps);
        Assert.Equal(2, result[0]);
    }

    [Fact]
    public void Dequantize8x8_ShouldPreserveNegativeCoefficients()
    {
        int[] coefficients = new int[64];
        coefficients[0] = -2;
        Assert.Equal(-10, H264InverseQuantizer.Dequantize8x8(
            coefficients, 0, Enumerable.Repeat(16, 64).ToArray())[0]);
    }

    [Fact]
    public void Dequantize8x8_ShouldRejectInvalidInputsAndScalingListIndices()
    {
        int[] flat = Enumerable.Repeat(16, 64).ToArray();
        Assert.Throws<ArgumentException>(() => H264InverseQuantizer.Dequantize8x8(new int[63], 0, flat));
        Assert.Throws<ArgumentException>(() => H264InverseQuantizer.Dequantize8x8(new int[64], 0, new int[63]));
        Assert.Throws<ArgumentOutOfRangeException>(() => H264InverseQuantizer.Dequantize8x8(new int[64], 88, flat));
        Assert.Throws<ArgumentOutOfRangeException>(() => H264InverseQuantizer.Dequantize8x8(
            new int[64], 0, flat, CreateSps(), CreatePps() with { Transform8X8ModeFlag = true }, 6));
        int[] invalid = Enumerable.Repeat(16, 64).ToArray();
        invalid[0] = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => H264InverseQuantizer.Dequantize8x8(new int[64], 0, invalid));
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
        // 26 + 25 reaches the valid maximum QPY of 51; a positive slice delta exceeds it.
        H264PictureParameterSetHeader pps = CreatePps() with { PicInitQpMinus26 = 25 };
        H264SliceHeader slice = CreateSlice() with { SliceQpDelta = 1 };
        Assert.Throws<FormatException>(() =>
            H264InverseQuantizer.CalculateLumaQpY(CreateSps(), pps, slice));
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

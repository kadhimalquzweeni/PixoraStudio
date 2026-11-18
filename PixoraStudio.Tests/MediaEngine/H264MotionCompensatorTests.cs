using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264MotionCompensatorTests
{
    private readonly H264MotionCompensator _compensator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InterpolateLuma_IntegerPosition_ShouldReturnReferenceSample(uint chromaFormat)
    {
        H264DecodedPictureSurface surface = CreateSurface(chromaFormat,
            (x, y) => (byte)(x + y * 2), (x, y) => (byte)(x + y),
            (x, y) => (byte)(x + y + 1));

        Assert.Equal((byte)17, _compensator.InterpolateLuma(surface, 5 * 4, 6 * 4));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(2, 2)]
    public void InterpolateLuma_HalfSampleOnLinearPlane_ShouldUseSixTapFilter(
        int quarterX, int quarterY)
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (x, y) => (byte)(x * 10 + y * 4),
            (_, _) => 64, (_, _) => 96);

        int x = 4 * 4 + quarterX;
        int y = 4 * 4 + quarterY;
        int expected = 4 * 10 + 4 * 4 + quarterX / 2 * 5 + quarterY / 2 * 2;

        Assert.Equal((byte)expected, _compensator.InterpolateLuma(surface, x, y));
    }

    [Theory]
    [InlineData(1, 0, 59)]
    [InlineData(3, 0, 64)]
    [InlineData(0, 1, 57)]
    [InlineData(0, 3, 59)]
    [InlineData(1, 1, 60)]
    [InlineData(3, 3, 67)]
    public void InterpolateLuma_QuarterSample_ShouldAverageAdjacentHalfGridSamples(
        int fractionX, int fractionY, byte expected)
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (x, y) => (byte)(x * 10 + y * 4),
            (_, _) => 64, (_, _) => 96);

        Assert.Equal(expected, _compensator.InterpolateLuma(
            surface, 4 * 4 + fractionX, 4 * 4 + fractionY));
    }

    [Theory]
    [InlineData(2, 0, 160)]
    [InlineData(0, 2, 160)]
    [InlineData(2, 2, 100)]
    public void InterpolateLuma_HalfSamples_ShouldUseNormativeSixTapCoefficients(
        int quarterX, int quarterY, byte expected)
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (x, y) => (quarterX, quarterY) switch
            {
                (2, 0) => x == 5 && y == 4 ? (byte)255 : (byte)0,
                (0, 2) => x == 4 && y == 5 ? (byte)255 : (byte)0,
                _ => x == 5 && y == 5 ? (byte)255 : (byte)0
            },
            (_, _) => 0, (_, _) => 0);

        Assert.Equal(expected, _compensator.InterpolateLuma(
            surface, 4 * 4 + quarterX, 4 * 4 + quarterY));
    }

    [Fact]
    public void InterpolateLuma_DiagonalHalfSample_ShouldApplyTwoDimensionalSixTapFilter()
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (x, y) => (byte)(x * 8 + y * 4),
            (_, _) => 64, (_, _) => 96);

        Assert.Equal((byte)54, _compensator.InterpolateLuma(surface, 4 * 4 + 2, 4 * 4 + 2));
    }

    [Theory]
    [InlineData(-1, -1, 0)]
    [InlineData(-4, 0, 0)]
    [InlineData(0, -4, 0)]
    [InlineData(31 * 4, 31 * 4, 62)]
    public void InterpolateLuma_OutOfFrameCoordinates_ShouldReplicateEdges(
        int x, int y, byte expected)
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (sx, sy) => (byte)(sx + sy), (_, _) => 0, (_, _) => 0);

        Assert.Equal(expected, _compensator.InterpolateLuma(surface, x, y));
    }

    [Theory]
    [InlineData(1, 4, 4, 72)]
    [InlineData(2, 4, 4, 72)]
    [InlineData(1, 0, 0, 48)]
    public void InterpolateChroma_ShouldUseBilinearEighthSampleInterpolation(
        uint format, int fractionX, int fractionY, byte expected)
    {
        H264DecodedPictureSurface surface = CreateSurface(format,
            (_, _) => 0,
            (x, y) => (byte)(x * 16 + y * 32),
            (x, y) => (byte)(100 + x * 8 + y * 16));

        Assert.Equal(expected, _compensator.InterpolateChroma(
            surface, 1, 8 + fractionX, 8 + fractionY));
    }

    [Fact]
    public void InterpolateChroma_ShouldRejectInvalidComponent()
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (_, _) => 0, (_, _) => 0, (_, _) => 0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _compensator.InterpolateChroma(surface, 0, 0, 0));
    }

    [Theory]
    [InlineData(1, 8, 8, 4, 24, 44)]
    [InlineData(2, 8, 8, 8, 26, 46)]
    public void Predict_ShouldProduceTightlyPackedLumaAndChromaPlanes(
        uint format, int width, int height, int chromaHeight, byte expectedCb, byte expectedCr)
    {
        H264DecodedPictureSurface surface = CreateSurface(format,
            (x, y) => (byte)(x + y),
            (x, y) => (byte)(x + y + 20),
            (x, y) => (byte)(x + y + 40));

        H264MotionCompensatedBlock result = _compensator.Predict(
            surface, 4, 4, width, height, new H264MotionVector(0, 0));

        Assert.Equal(width, result.LumaWidth);
        Assert.Equal(height, result.LumaHeight);
        Assert.Equal(width / 2, result.ChromaWidth);
        Assert.Equal(chromaHeight, result.ChromaHeight);
        Assert.Equal(width * height, result.Luma.Length);
        Assert.Equal(width / 2 * chromaHeight, result.Cb.Length);
        Assert.Equal(width / 2 * chromaHeight, result.Cr.Length);
        Assert.Equal((byte)8, result.Luma[0]);
        Assert.Equal(expectedCb, result.Cb[0]);
        Assert.Equal(expectedCr, result.Cr[0]);
    }

    [Fact]
    public void Predict_ShouldApplyNegativeMotionVectorUsingEdgeReplication()
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (x, y) => (byte)(x + y), (_, _) => 30, (_, _) => 40);

        H264MotionCompensatedBlock result = _compensator.Predict(
            surface, 0, 0, 4, 4, new H264MotionVector(-4, -4));

        Assert.Equal((byte)0, result.Luma[0]);
    }

    [Fact]
    public void Predict_ShouldRejectInvalidPartitionGeometry()
    {
        H264DecodedPictureSurface surface = CreateSurface(1,
            (_, _) => 0, (_, _) => 0, (_, _) => 0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _compensator.Predict(surface, 1, 0, 4, 4, new H264MotionVector(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _compensator.Predict(surface, 0, 0, 3, 4, new H264MotionVector(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _compensator.Predict(surface, 30, 0, 4, 4, new H264MotionVector(0, 0)));
    }

    private static H264DecodedPictureSurface CreateSurface(
        uint format,
        Func<int, int, byte> lumaValue,
        Func<int, int, byte> cbValue,
        Func<int, int, byte> crValue)
    {
        H264SequenceParameterSet sps = new(
            0, 66, 30, format, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);
        H264DecodedPictureSurface surface = new(sps);
        int chromaHeight = format == 1 ? 16 : 32;

        for (int macroblockY = 0; macroblockY < 2; macroblockY++)
        for (int macroblockX = 0; macroblockX < 2; macroblockX++)
        {
            byte[] y = new byte[256];
            byte[] cb = new byte[8 * (format == 1 ? 8 : 16)];
            byte[] cr = new byte[cb.Length];

            for (int row = 0; row < 16; row++)
            for (int column = 0; column < 16; column++)
                y[row * 16 + column] =
                    lumaValue(macroblockX * 16 + column, macroblockY * 16 + row);

            int blockChromaHeight = format == 1 ? 8 : 16;
            for (int row = 0; row < blockChromaHeight; row++)
            for (int column = 0; column < 8; column++)
            {
                int globalX = macroblockX * 8 + column;
                int globalY = macroblockY * blockChromaHeight + row;
                cb[row * 8 + column] = cbValue(globalX, globalY);
                cr[row * 8 + column] = crValue(globalX, globalY);
            }

            surface.WriteMacroblock(
                macroblockX, macroblockY, 0, false, y, cb, cr);
        }

        Assert.Equal(chromaHeight, surface.ChromaHeight);
        return surface;
    }
}

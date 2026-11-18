using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Intra8x8ResidualReconstructorTests
{
    private static readonly byte[] Prediction = Enumerable.Repeat((byte)128, 64).ToArray();

    [Fact]
    public void Reconstruct_ShouldPreservePredictionForZeroResidual()
    {
        byte[] result = H264Intra8x8ResidualReconstructor.Reconstruct(
            Prediction, new int[64], 0, CreateSps(), CreatePps());

        Assert.Equal(Prediction, result);
    }

    [Fact]
    public void Reconstruct_ShouldApplyInverseQuantisationAndTransformToDcCoefficient()
    {
        int[] coefficients = new int[64];
        coefficients[0] = 1;

        byte[] result = H264Intra8x8ResidualReconstructor.Reconstruct(
            Prediction, coefficients, 0, CreateSps(), CreatePps());

        // Default 8x8 intra scaling list, QP 0: the DC level dequantises
        // to 2 and the inverse transform contributes one to each sample.
        Assert.All(result, sample => Assert.Equal((byte)129, sample));
    }

    [Fact]
    public void Reconstruct_ShouldClipPositiveAndNegativeResidualsToEightBitRange()
    {
        int[] positive = new int[64];
        positive[0] = 1000;
        int[] negative = new int[64];
        negative[0] = -1000;

        byte[] high = H264Intra8x8ResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)250, 64).ToArray(), positive,
            0, CreateSps(), CreatePps());
        byte[] low = H264Intra8x8ResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)5, 64).ToArray(), negative,
            0, CreateSps(), CreatePps());

        Assert.All(high, sample => Assert.Equal(byte.MaxValue, sample));
        Assert.All(low, sample => Assert.Equal((byte)0, sample));
    }

    [Fact]
    public void Reconstruct_ShouldRespectFieldScanCoefficientOrdering()
    {
        int[] scan = new int[64];
        scan[1] = 2;
        int[] raster = H264CoefficientScanner.ToRasterOrder(scan, fieldScan: true);
        byte[] expected = H264IntraPrediction.AddResidual(
            Prediction,
            H264InverseTransform.Inverse8x8(
                H264InverseQuantizer.Dequantize8x8(raster, 0, CreateSps(), CreatePps())));

        byte[] actual = H264Intra8x8ResidualReconstructor.Reconstruct(
            Prediction, scan, 0, CreateSps(), CreatePps(), fieldScan: true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Reconstruct_ShouldRejectInvalidBlockSizesAndDisabledTransform()
    {
        Assert.Throws<ArgumentException>(() =>
            H264Intra8x8ResidualReconstructor.Reconstruct(
                new byte[63], new int[64], 0, CreateSps(), CreatePps()));
        Assert.Throws<ArgumentException>(() =>
            H264Intra8x8ResidualReconstructor.Reconstruct(
                Prediction, new int[63], 0, CreateSps(), CreatePps()));
        Assert.Throws<FormatException>(() =>
            H264Intra8x8ResidualReconstructor.Reconstruct(
                Prediction, new int[64], 0, CreateSps(), CreatePps() with
                { Transform8X8ModeFlag = false }));
    }

    [Fact]
    public void Reconstruct_ShouldRejectMismatchedParameterSetsAndUnsupportedBitDepth()
    {
        Assert.Throws<ArgumentException>(() =>
            H264Intra8x8ResidualReconstructor.Reconstruct(
                Prediction, new int[64], 0, CreateSps(),
                CreatePps() with { SequenceParameterSetId = 1 }));
        Assert.Throws<NotSupportedException>(() =>
            H264Intra8x8ResidualReconstructor.Reconstruct(
                Prediction, new int[64], 0,
                CreateSps() with { BitDepthLumaMinus8 = 1 }, CreatePps()));
    }

    [Fact]
    public void Reconstruct_ShouldRejectOutOfRangeQpAndInvalidScalingListIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264Intra8x8ResidualReconstructor.Reconstruct(
                Prediction, new int[64], 88, CreateSps(), CreatePps()));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264Intra8x8ResidualReconstructor.Reconstruct(
                Prediction, new int[64], 0, CreateSps(), CreatePps(),
                scalingListIndex: 8));
    }

    private static H264SequenceParameterSet CreateSps() => new(
        0, 66, 30, 1, false, 0, 0, 0, 0, 0, 2,
        39, 22, true, true, 640, 368, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0) with { Transform8X8ModeFlag = true };
}

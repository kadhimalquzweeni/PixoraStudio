using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraResidualReconstructorTests
{
    [Fact]
    public void Reconstruct4x4_ShouldPreservePredictionForZeroResidual()
    {
        byte[] prediction = Enumerable.Range(0, 16).Select(i => (byte)(i * 10)).ToArray();
        H264CavlcResidualBlock residual = CreateResidual(16, new int[16]);

        byte[] result = H264IntraResidualReconstructor.Reconstruct4x4(
            prediction, residual, 0, CreateSps(), CreatePps());

        Assert.Equal(prediction, result);
    }

    [Fact]
    public void Reconstruct4x4_ShouldInverseQuantiseAndTransformFullResidualBlock()
    {
        int[] coefficients = new int[16];
        coefficients[0] = 1;
        H264CavlcResidualBlock residual = CreateResidual(16, coefficients);

        byte[] result = H264IntraResidualReconstructor.Reconstruct4x4(
            Enumerable.Repeat((byte)100, 16).ToArray(),
            residual, 24, CreateSps(), CreatePps());

        // With the flat default intra scaling list and QP-prime 24,
        // the DC coefficient dequantises to 160. The inverse transform
        // contributes three to every reconstructed sample.
        Assert.Equal(Enumerable.Repeat((byte)103, 16), result);
    }

    [Fact]
    public void Reconstruct4x4_ShouldMapIntra16x16AcCoefficientsAfterDcPosition()
    {
        int[] coefficients = new int[15];
        coefficients[0] = 1;
        H264CavlcResidualBlock residual = CreateResidual(15, coefficients);

        byte[] result = H264IntraResidualReconstructor.Reconstruct4x4(
            Enumerable.Repeat((byte)100, 16).ToArray(),
            residual, 24, CreateSps(), CreatePps());

        // The first AC coefficient is scan position 1 (raster position 1),
        // not the DC position. At QP-prime 24 its transformed residual is
        // [3, 2, -2, -3] on each row.
        Assert.Equal(new byte[]
        {
            103, 102, 98, 97,
            103, 102, 98, 97,
            103, 102, 98, 97,
            103, 102, 98, 97
        }, result);
    }

    [Fact]
    public void Reconstruct4x4_ShouldClipReconstructedSamples()
    {
        int[] coefficients = new int[16];
        coefficients[0] = 1000;
        byte[] result = H264IntraResidualReconstructor.Reconstruct4x4(
            Enumerable.Repeat((byte)250, 16).ToArray(),
            CreateResidual(16, coefficients), 0, CreateSps(), CreatePps());

        Assert.All(result, sample => Assert.Equal((byte)255, sample));
    }

    [Fact]
    public void Reconstruct4x4_ShouldValidateBlockShapeAndParameterSets()
    {
        Assert.Throws<ArgumentException>(() =>
            H264IntraResidualReconstructor.Reconstruct4x4(
                new byte[15], CreateResidual(16, new int[16]), 0, CreateSps(), CreatePps()));
        Assert.Throws<ArgumentException>(() =>
            H264IntraResidualReconstructor.Reconstruct4x4(
                new byte[16], CreateResidual(8, new int[8]), 0, CreateSps(), CreatePps()));
        Assert.Throws<FormatException>(() =>
            H264IntraResidualReconstructor.Reconstruct4x4(
                new byte[16], CreateResidual(16, new int[16]), 0,
                CreateSps(), CreatePps() with { SequenceParameterSetId = 1 }));
    }

    private static H264CavlcResidualBlock CreateResidual(int maximum, int[] coefficients) =>
        new(maximum, coefficients.Count(value => value != 0), 0, 0, coefficients);

    private static H264SequenceParameterSet CreateSps() =>
        new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);
}

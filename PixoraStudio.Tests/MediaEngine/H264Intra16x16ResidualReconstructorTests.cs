using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Intra16x16ResidualReconstructorTests
{
    [Fact]
    public void InverseHadamard4x4_ShouldReturnZeroForZeroInput()
    {
        Assert.Equal(new int[16], H264Intra16x16ResidualReconstructor.InverseHadamard4x4(new int[16]));
    }

    [Fact]
    public void InverseHadamard4x4_ShouldDistributeSingleDcLevelAcrossAllPositions()
    {
        int[] input = new int[16];
        input[0] = 1;

        Assert.Equal(Enumerable.Repeat(1, 16),
            H264Intra16x16ResidualReconstructor.InverseHadamard4x4(input));
    }

    [Fact]
    public void InverseHadamard4x4_ShouldApplyExpectedSignedHadamardPattern()
    {
        int[] input = new int[16];
        input[0] = 1;
        input[1] = 2;

        Assert.Equal(new[]
        {
            3, 3, -1, -1,
            3, 3, -1, -1,
            3, 3, -1, -1,
            3, 3, -1, -1
        }, H264Intra16x16ResidualReconstructor.InverseHadamard4x4(input));
    }

    [Fact]
    public void InverseHadamard4x4_ShouldPreserveDistinctHorizontalFrequencyOrder()
    {
        int[] input = new int[16];
        input[0] = 1;
        input[1] = 2;
        input[2] = 4;
        input[3] = 8;

        int[] expectedRow = [15, -9, -5, 3];
        int[] expected = Enumerable.Range(0, 4)
            .SelectMany(_ => expectedRow)
            .ToArray();

        Assert.Equal(expected,
            H264Intra16x16ResidualReconstructor.InverseHadamard4x4(input));
    }

    [Fact]
    public void Reconstruct_ShouldPreservePredictionWhenDcAndAcAreZero()
    {
        byte[] prediction = Enumerable.Range(0, 256).Select(i => (byte)(i % 256)).ToArray();

        byte[] result = H264Intra16x16ResidualReconstructor.Reconstruct(
            prediction, CreateResidual(16, new int[16]), CreateAcBlocks(),
            24, CreateSps(), CreatePps());

        Assert.Equal(prediction, result);
    }

    [Fact]
    public void Reconstruct_ShouldApplyDcResidualAcrossAllSixteenLumaBlocks()
    {
        int[] dc = new int[16];
        dc[0] = 1;

        byte[] result = H264Intra16x16ResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)100, 256).ToArray(),
            CreateResidual(16, dc), CreateAcBlocks(),
            24, CreateSps(), CreatePps());

        Assert.Equal(256, result.Length);
        Assert.All(result, sample => Assert.True(sample > 100));
        Assert.Single(result.Distinct());
    }

    [Fact]
    public void Reconstruct_ShouldApplyAcResidualInItsOwnBlock()
    {
        H264CavlcResidualBlock[] ac = CreateAcBlocks().ToArray();
        int[] firstAc = new int[15];
        firstAc[0] = 1;
        ac[0] = CreateResidual(15, firstAc);

        byte[] result = H264Intra16x16ResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)100, 256).ToArray(),
            CreateResidual(16, new int[16]), ac,
            24, CreateSps(), CreatePps());

        // The first luma4x4BlkIdx block occupies the top-left 4x4 region.
        for (int y = 0; y < 4; y++)
        {
            Assert.Equal(new byte[] { 104, 102, 98, 96 },
                result.Skip(y * 16).Take(4));
        }

        for (int y = 0; y < 16; y++)
        {
            Assert.All(result.Skip(y * 16 + 4).Take(12),
                sample => Assert.Equal((byte)100, sample));
        }
        for (int y = 4; y < 16; y++)
        {
            Assert.All(result.Skip(y * 16).Take(4),
                sample => Assert.Equal((byte)100, sample));
        }
    }

    [Fact]
    public void Reconstruct_ShouldPlaceAllAcBlocksUsingH264MacroblockBlockScanOrder()
    {
        H264CavlcResidualBlock[] ac = Enumerable.Range(0, 16)
            .Select(_ =>
            {
                int[] levels = new int[15];
                levels[0] = 1;
                return CreateResidual(15, levels);
            })
            .ToArray();

        byte[] result = H264Intra16x16ResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)100, 256).ToArray(),
            CreateResidual(16, new int[16]), ac,
            24, CreateSps(), CreatePps());

        for (int blockY = 0; blockY < 4; blockY++)
        {
            for (int blockX = 0; blockX < 4; blockX++)
            {
                for (int y = 0; y < 4; y++)
                {
                    Assert.Equal(new byte[] { 104, 102, 98, 96 },
                        result.Skip((blockY * 4 + y) * 16 + blockX * 4).Take(4));
                }
            }
        }
    }

    [Fact]
    public void Reconstruct_ShouldRejectMalformedAcResidualBlock()
    {
        H264CavlcResidualBlock[] ac = CreateAcBlocks().ToArray();
        ac[7] = new H264CavlcResidualBlock(15, 1, 0, 0, new int[15]);

        Assert.Throws<FormatException>(() =>
            H264Intra16x16ResidualReconstructor.Reconstruct(
                new byte[256], CreateResidual(16, new int[16]), ac,
                24, CreateSps(), CreatePps()));
    }

    [Fact]
    public void Reconstruct_ShouldCombineDcAndAcInSameTransformBlock()
    {
        int[] dc = new int[16];
        dc[0] = 1;
        H264CavlcResidualBlock[] ac = CreateAcBlocks().ToArray();
        int[] firstAc = new int[15];
        firstAc[0] = 1;
        ac[0] = CreateResidual(15, firstAc);

        byte[] result = H264Intra16x16ResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)100, 256).ToArray(),
            CreateResidual(16, dc), ac, 24, CreateSps(), CreatePps());

        Assert.Equal(256, result.Length);
        Assert.Contains(result, sample => sample != 100);
        Assert.All(result, sample => Assert.InRange(sample, (byte)0, (byte)255));
    }

    [Fact]
    public void Reconstruct_ShouldClipAtEightBitBoundaries()
    {
        int[] dc = new int[16];
        dc[0] = 1000;

        byte[] result = H264Intra16x16ResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)250, 256).ToArray(),
            CreateResidual(16, dc), CreateAcBlocks(),
            24, CreateSps(), CreatePps());

        Assert.All(result, sample => Assert.Equal((byte)255, sample));
    }

    [Fact]
    public void Reconstruct_ShouldRejectInvalidMacroblockShapeAndResidualCounts()
    {
        Assert.Throws<ArgumentException>(() =>
            H264Intra16x16ResidualReconstructor.Reconstruct(
                new byte[255], CreateResidual(16, new int[16]), CreateAcBlocks(),
                0, CreateSps(), CreatePps()));

        Assert.Throws<ArgumentException>(() =>
            H264Intra16x16ResidualReconstructor.Reconstruct(
                new byte[256], CreateResidual(16, new int[16]), CreateAcBlocks().Take(15).ToArray(),
                0, CreateSps(), CreatePps()));

        Assert.Throws<ArgumentException>(() =>
            H264Intra16x16ResidualReconstructor.Reconstruct(
                new byte[256], CreateResidual(15, new int[15]), CreateAcBlocks(),
                0, CreateSps(), CreatePps()));
    }

    [Fact]
    public void Reconstruct_ShouldRejectInconsistentTotalCoefficientCount()
    {
        H264CavlcResidualBlock invalid = new(16, 1, 0, 0, new int[16]);

        Assert.Throws<FormatException>(() =>
            H264Intra16x16ResidualReconstructor.Reconstruct(
                new byte[256], invalid, CreateAcBlocks(),
                0, CreateSps(), CreatePps()));
    }

    private static H264CavlcResidualBlock[] CreateAcBlocks() =>
        Enumerable.Range(0, 16).Select(_ => CreateResidual(15, new int[15])).ToArray();

    private static H264CavlcResidualBlock CreateResidual(int maximum, int[] coefficients) =>
        new(maximum, coefficients.Count(value => value != 0), 0, 0, coefficients);

    private static H264SequenceParameterSet CreateSps() =>
        new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);
}

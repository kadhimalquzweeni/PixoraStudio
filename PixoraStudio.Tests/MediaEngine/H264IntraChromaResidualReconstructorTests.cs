using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraChromaResidualReconstructorTests
{
    [Theory]
    [InlineData(1, 64, 4)]
    [InlineData(2, 128, 8)]
    public void Reconstruct_ShouldPreservePredictionForZeroResiduals(uint chromaFormat, int sampleCount, int blockCount)
    {
        var sps = CreateSps(chromaFormat);
        var component = CreateComponent(1, blockCount, chromaFormat == 1 ? 4 : 8, null);
        byte[] prediction = Enumerable.Range(0, sampleCount).Select(i => (byte)(i % 256)).ToArray();

        byte[] result = H264IntraChromaResidualReconstructor.Reconstruct(
            prediction, component, 24, sps, CreatePps());

        Assert.Equal(prediction, result);
    }

    [Fact]
    public void Reconstruct420_ShouldApplyDcResidualToAllFourBlocks()
    {
        int[] levels = new int[4];
        levels[0] = 1;
        var component = CreateComponent(1, 4, 4, CreateResidual(4, levels));

        byte[] result = H264IntraChromaResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)100, 64).ToArray(), component, 24,
            CreateSps(1), CreatePps());

        Assert.Equal(64, result.Length);
        Assert.All(result, sample => Assert.Equal((byte)101, sample));
    }

    [Fact]
    public void Reconstruct422_ShouldApplyDcResidualToAllEightBlocks()
    {
        int[] levels = new int[8];
        levels[0] = 1;
        var component = CreateComponent(1, 8, 8, CreateResidual(8, levels));

        byte[] result = H264IntraChromaResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)100, 128).ToArray(), component, 24,
            CreateSps(2), CreatePps());

        Assert.Equal(128, result.Length);
        Assert.All(result, sample => Assert.Equal((byte)101, sample));
    }

    [Fact]
    public void Reconstruct_ShouldApplyAcOnlyToItsOwnBlock()
    {
        H264IntraChromaResidualBlock[] blocks = CreateBlocks(4).ToArray();
        int[] ac = new int[15];
        ac[0] = 1;
        blocks[0] = new H264IntraChromaResidualBlock(0, 0, true, CreateResidual(15, ac));
        var component = new H264IntraChromaResidualComponent(1, null, blocks);

        byte[] result = H264IntraChromaResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)100, 64).ToArray(), component, 24,
            CreateSps(1), CreatePps());

        for (int y = 0; y < 4; y++)
            Assert.Equal(new byte[] { 103, 102, 98, 97 }, result.Skip(y * 8).Take(4));
        for (int y = 0; y < 8; y++)
            Assert.All(result.Skip(y * 8 + (y < 4 ? 4 : 0)).Take(y < 4 ? 4 : 8),
                sample => Assert.Equal((byte)100, sample));
    }

    [Fact]
    public void CalculateChromaQpPrime_ShouldApplyCbAndCrOffsetsAndNonlinearMapping()
    {
        var sps = CreateSps(1);
        var pps = CreatePps() with
        {
            ChromaQpIndexOffset = 2,
            SecondChromaQpIndexOffset = -2
        };

        Assert.Equal(32, H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(32, 1, sps, pps));
        Assert.Equal(29, H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(32, 2, sps, pps));
    }

    [Fact]
    public void CalculateChromaQpPrime_ShouldClipIndexBeforeMapping()
    {
        Assert.Equal(0, H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(
            0, 1, CreateSps(1), CreatePps() with { ChromaQpIndexOffset = -12 }));
        Assert.Equal(39, H264IntraChromaResidualReconstructor.CalculateChromaQpPrime(
            51, 1, CreateSps(1), CreatePps() with { ChromaQpIndexOffset = 12 }));
    }

    [Fact]
    public void InverseChromaDc2x2_ShouldApplyExpectedHadamardPattern()
    {
        Assert.Equal(new[] { 3, -1, 3, -1 },
            H264IntraChromaResidualReconstructor.InverseChromaDc2x2(new[] { 1, 2, 0, 0 }));
    }

    [Fact]
    public void InverseChromaDc2x4_ShouldTransformBothFrequencyDimensions()
    {
        Assert.Equal(Enumerable.Repeat(1, 8),
            H264IntraChromaResidualReconstructor.InverseChromaDc2x4(
                new[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void Reconstruct_ShouldClipAtEightBitBoundaries()
    {
        int[] dc = new int[4];
        dc[0] = 1000;
        var component = CreateComponent(1, 4, 4, CreateResidual(4, dc));

        byte[] result = H264IntraChromaResidualReconstructor.Reconstruct(
            Enumerable.Repeat((byte)250, 64).ToArray(), component, 51,
            CreateSps(1), CreatePps());

        Assert.All(result, sample => Assert.Equal((byte)255, sample));
    }

    [Fact]
    public void Reconstruct_ShouldRejectWrongPredictionSizeAndBlockOrder()
    {
        var component = CreateComponent(1, 4, 4, null);
        Assert.Throws<ArgumentException>(() =>
            H264IntraChromaResidualReconstructor.Reconstruct(
                new byte[63], component, 24, CreateSps(1), CreatePps()));

        H264IntraChromaResidualBlock[] blocks = CreateBlocks(4).ToArray();
        blocks[1] = blocks[1] with { BlockX = 0 };
        component = new H264IntraChromaResidualComponent(1, null, blocks);
        Assert.Throws<FormatException>(() =>
            H264IntraChromaResidualReconstructor.Reconstruct(
                new byte[64], component, 24, CreateSps(1), CreatePps()));
    }

    [Fact]
    public void Reconstruct_ShouldUseComponentAsParameterNameForNullAcBlock()
    {
        H264IntraChromaResidualBlock[] blocks = CreateBlocks(4).ToArray();
        blocks[0] = null!;
        var component = new H264IntraChromaResidualComponent(1, null, blocks);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            H264IntraChromaResidualReconstructor.Reconstruct(
                new byte[64], component, 24, CreateSps(1), CreatePps()));

        Assert.Equal("component", exception.ParamName);
    }

    [Fact]
    public void Reconstruct_ShouldRejectMalformedResidualAndUnsupportedFormats()
    {
        var invalid = new H264IntraChromaResidualComponent(
            1, new H264CavlcResidualBlock(4, 1, 0, 0, new int[4]), CreateBlocks(4));
        Assert.Throws<FormatException>(() =>
            H264IntraChromaResidualReconstructor.Reconstruct(
                new byte[64], invalid, 24, CreateSps(1), CreatePps()));

        Assert.Throws<NotSupportedException>(() =>
            H264IntraChromaResidualReconstructor.Reconstruct(
                new byte[256], CreateComponent(1, 16, 16, null), 24,
                CreateSps(3), CreatePps()));
    }

    private static H264IntraChromaResidualComponent CreateComponent(
        byte id, int blockCount, int dcCapacity, H264CavlcResidualBlock? dc) =>
        new(id, dc, CreateBlocks(blockCount));

    private static IReadOnlyList<H264IntraChromaResidualBlock> CreateBlocks(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new H264IntraChromaResidualBlock(
                i % 2, i / 2, false, CreateResidual(15, new int[15])))
            .ToArray();

    private static H264CavlcResidualBlock CreateResidual(int maximum, int[] coefficients) =>
        new(maximum, coefficients.Count(value => value != 0), 0, 0, coefficients);

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) =>
        new(0, 100, 40, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);
}

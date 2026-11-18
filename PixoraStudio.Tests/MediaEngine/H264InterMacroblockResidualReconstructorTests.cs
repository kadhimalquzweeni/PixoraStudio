using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterMacroblockResidualReconstructorTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Reconstruct_WithZeroResiduals_ShouldPreservePrediction(
        uint chromaFormat, int chromaHeight)
    {
        H264MotionCompensatedBlock prediction = Prediction(chromaHeight);
        H264MotionCompensatedBlock result = H264InterMacroblockResidualReconstructor.Reconstruct(
            prediction, Residual(chromaFormat), 24, Sps(chromaFormat), Pps());

        Assert.Equal(prediction.Luma, result.Luma);
        Assert.Equal(prediction.Cb, result.Cb);
        Assert.Equal(prediction.Cr, result.Cr);
        Assert.NotSame(prediction.Luma, result.Luma);
        Assert.NotSame(prediction.Cb, result.Cb);
        Assert.NotSame(prediction.Cr, result.Cr);
    }

    [Fact]
    public void Reconstruct_ShouldApplyLumaResidualToItsOwnFourByFourRegion()
    {
        H264MotionCompensatedBlock prediction = Prediction(8);
        H264IntraLumaResidualBlock[] blocks = EmptyLumaBlocks();
        int[] levels = new int[16];
        levels[0] = 1;
        blocks[0] = new H264IntraLumaResidualBlock(
            0, 0, true, new H264CavlcResidualBlock(16, 1, 0, 0, levels));
        H264InterResidualMacroblock residual = Residual(1) with { LumaBlocks = blocks };

        H264MotionCompensatedBlock result = H264InterMacroblockResidualReconstructor.Reconstruct(
            prediction, residual, 24, Sps(1), Pps());

        byte[] expectedBlock = H264IntraResidualReconstructor.Reconstruct4x4(
            Enumerable.Repeat((byte)100, 16).ToArray(),
            blocks[0].Residual, 24, Sps(1), Pps(), scalingListIndex: 3);
        for (int y = 0; y < 4; y++)
            Assert.Equal(expectedBlock.Skip(y * 4).Take(4), result.Luma.Skip(y * 16).Take(4));
        for (int y = 0; y < 16; y++)
            Assert.All(result.Luma.Skip(y * 16 + 4).Take(12),
                sample => Assert.Equal((byte)100, sample));
    }

    [Fact]
    public void Reconstruct_ShouldRejectDuplicateOrIncompleteLumaBlockGrid()
    {
        H264IntraLumaResidualBlock[] blocks = EmptyLumaBlocks();
        blocks[1] = blocks[1] with { BlockX = 0, BlockY = 0 };
        Assert.Throws<FormatException>(() =>
            H264InterMacroblockResidualReconstructor.Reconstruct(
                Prediction(8), Residual(1) with { LumaBlocks = blocks },
                24, Sps(1), Pps()));

        Assert.Throws<FormatException>(() =>
            H264InterMacroblockResidualReconstructor.Reconstruct(
                Prediction(8), Residual(1) with { LumaBlocks = blocks[..15] },
                24, Sps(1), Pps()));
    }

    [Fact]
    public void Reconstruct_ShouldRejectWrongPredictionGeometryAndUnsupportedSettings()
    {
        H264InterResidualMacroblock residual = Residual(1);
        Assert.Throws<ArgumentException>(() =>
            H264InterMacroblockResidualReconstructor.Reconstruct(
                Prediction(8) with { LumaWidth = 8 }, residual, 24, Sps(1), Pps()));
        Assert.Throws<NotSupportedException>(() =>
            H264InterMacroblockResidualReconstructor.Reconstruct(
                Prediction(8), residual, 24, Sps(1) with { FrameMbsOnlyFlag = false }, Pps()));
        Assert.Throws<NotSupportedException>(() =>
            H264InterMacroblockResidualReconstructor.Reconstruct(
                Prediction(8), residual, 24, Sps(1), Pps() with { EntropyCodingModeFlag = true }));
    }

    private static H264InterResidualMacroblock Residual(uint format)
    {
        int blocksHigh = format == 1 ? 2 : 4;
        int blockCount = blocksHigh * 2;
        H264IntraChromaResidualComponent Component(byte id) =>
            new(id, null, Enumerable.Range(0, blockCount)
                .Select(i => new H264IntraChromaResidualBlock(
                    i % 2, i / 2, false,
                    new H264CavlcResidualBlock(15, 0, 0, 0, new int[15])))
                .ToArray());
        return new H264InterResidualMacroblock(
            0, EmptyLumaBlocks(), [Component(1), Component(2)]);
    }

    private static H264IntraLumaResidualBlock[] EmptyLumaBlocks() =>
        Enumerable.Range(0, 16)
            .Select(i => new H264IntraLumaResidualBlock(
                i % 4, i / 4, false,
                new H264CavlcResidualBlock(16, 0, 0, 0, new int[16])))
            .ToArray();

    private static H264MotionCompensatedBlock Prediction(int chromaHeight) =>
        new(16, 16, 8, chromaHeight,
            Enumerable.Repeat((byte)100, 256).ToArray(),
            Enumerable.Repeat((byte)110, 8 * chromaHeight).ToArray(),
            Enumerable.Repeat((byte)120, 8 * chromaHeight).ToArray());

    private static H264SequenceParameterSet Sps(uint format) =>
        new(0, 100, 40, format, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader Pps() =>
        new(0, 0, false, false, 0);
}

using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterPredictionAssemblerTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Assemble_ShouldPlaceSingleFullMacroblockPrediction(uint format, int chromaHeight)
    {
        H264MotionCompensatedBlock source = Block(16, 16, 8, chromaHeight, 10, 20, 30);
        H264MotionCompensatedBlock result = H264InterPredictionAssembler.Assemble(
            [Prediction(0, 0, 4, 4, source)], format);

        Assert.Equal(source.Luma, result.Luma);
        Assert.Equal(source.Cb, result.Cb);
        Assert.Equal(source.Cr, result.Cr);
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Assemble_ShouldPlaceFourEightByEightPartitions(uint format, int chromaHeight)
    {
        List<H264InterMacroblockPrediction> predictions = [];
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
        {
            byte value = (byte)(10 + y * 2 + x);
            predictions.Add(Prediction(x * 2, y * 2, 2, 2,
                Block(8, 8, 4, chromaHeight / 2, value, value, value)));
        }

        H264MotionCompensatedBlock result =
            H264InterPredictionAssembler.Assemble(predictions, format);

        Assert.Equal(10, result.Luma[0]);
        Assert.Equal(11, result.Luma[8]);
        Assert.Equal(12, result.Luma[8 * 16]);
        Assert.Equal(13, result.Luma[8 * 16 + 8]);
        Assert.Equal(10, result.Cb[0]);
        Assert.Equal(11, result.Cb[4]);
        Assert.Equal(12, result.Cb[(chromaHeight / 2) * 8]);
        Assert.Equal(13, result.Cb[(chromaHeight / 2) * 8 + 4]);
    }

    [Fact]
    public void Assemble_ShouldRejectGapsAndOverlappingPartitions()
    {
        H264InterMacroblockPrediction first = Prediction(0, 0, 2, 4,
            Block(8, 16, 4, 8, 1, 1, 1));
        H264InterMacroblockPrediction second = Prediction(0, 0, 2, 4,
            Block(8, 16, 4, 8, 2, 2, 2));

        Assert.Throws<FormatException>(() =>
            H264InterPredictionAssembler.Assemble([first], 1));
        Assert.Throws<FormatException>(() =>
            H264InterPredictionAssembler.Assemble([first, second], 1));
    }

    [Fact]
    public void Assemble_ShouldRejectPartitionGeometryAndBufferMismatches()
    {
        H264InterMacroblockPrediction wrongSize = Prediction(0, 0, 4, 4,
            Block(8, 16, 4, 8, 1, 2, 3));
        H264InterMacroblockPrediction outside = Prediction(3, 0, 2, 4,
            Block(8, 16, 4, 8, 1, 2, 3));

        Assert.Throws<FormatException>(() =>
            H264InterPredictionAssembler.Assemble([wrongSize], 1));
        Assert.Throws<FormatException>(() =>
            H264InterPredictionAssembler.Assemble([outside], 1));
        Assert.Throws<NotSupportedException>(() =>
            H264InterPredictionAssembler.Assemble([], 3));
        Assert.Throws<ArgumentException>(() =>
            H264InterPredictionAssembler.Assemble([], 1));
    }

    private static H264InterMacroblockPrediction Prediction(
        int x, int y, int width, int height, H264MotionCompensatedBlock block) =>
        new(new H264ReconstructedInterPartition(
            0, 0, x, y, width, height, 0, new H264MotionVector(0, 0)), block);

    private static H264MotionCompensatedBlock Block(
        int yWidth, int yHeight, int cWidth, int cHeight,
        byte y, byte cb, byte cr) =>
        new(yWidth, yHeight, cWidth, cHeight,
            Enumerable.Repeat(y, yWidth * yHeight).ToArray(),
            Enumerable.Repeat(cb, cWidth * cHeight).ToArray(),
            Enumerable.Repeat(cr, cWidth * cHeight).ToArray());
}

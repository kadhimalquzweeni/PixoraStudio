using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264ChromaIntraPredictionTests
{
    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void Predict_Horizontal_ShouldReplicateLeftSamplesAcrossRows(uint chromaFormat, int expectedLength)
    {
        byte[] top = Enumerable.Range(0, 8).Select(i => (byte)(10 + i)).ToArray();
        byte[] left = Enumerable.Range(0, chromaFormat == 1 ? 8 : 16)
            .Select(i => (byte)(30 + i)).ToArray();

        byte[] result = H264ChromaIntraPrediction.Predict(
            1, chromaFormat, top, left, 20, true, true, true);

        Assert.Equal(expectedLength, result.Length);
        int height = chromaFormat == 1 ? 8 : 16;
        for (int y = 0; y < height; y++)
            Assert.All(result.Skip(y * 8).Take(8), sample => Assert.Equal(left[y], sample));
    }

    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void Predict_Vertical_ShouldReplicateTopSamplesAcrossRows(uint chromaFormat, int expectedLength)
    {
        byte[] top = [11, 22, 33, 44, 55, 66, 77, 88];
        byte[] left = new byte[chromaFormat == 1 ? 8 : 16];

        byte[] result = H264ChromaIntraPrediction.Predict(
            2, chromaFormat, top, left, 0, true, true, true);

        Assert.Equal(expectedLength, result.Length);
        int height = chromaFormat == 1 ? 8 : 16;
        for (int y = 0; y < height; y++)
            Assert.Equal(top, result.Skip(y * 8).Take(8));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Predict_DcWithBothEdges_ShouldUseCorrectPerRegionAverages(uint chromaFormat, int height)
    {
        byte[] top = Enumerable.Repeat((byte)40, 8).ToArray();
        byte[] left = Enumerable.Repeat((byte)80, height).ToArray();

        byte[] result = H264ChromaIntraPrediction.Predict(
            0, chromaFormat, top, left, 0, true, true, false);

        int blockRows = height / 4;
        for (int blockY = 0; blockY < blockRows; blockY++)
        {
            for (int blockX = 0; blockX < 2; blockX++)
            {
                byte expected = blockY == 0
                    ? (blockX == 0 ? (byte)60 : (byte)40)
                    : (blockX == 0 ? (byte)80 : (byte)60);
                for (int y = blockY * 4; y < blockY * 4 + 4; y++)
                    Assert.All(result.Skip(y * 8 + blockX * 4).Take(4),
                        sample => Assert.Equal(expected, sample));
            }
        }
    }

    [Fact]
    public void Predict_Dc420_ShouldUseTopOnlyLeftOnlyAndCombinedRegions()
    {
        byte[] top = [20, 20, 20, 20, 60, 60, 60, 60];
        byte[] left = [100, 100, 100, 100, 140, 140, 140, 140];

        byte[] result = H264ChromaIntraPrediction.Predict(
            0, 1, top, left, 0, true, true, false);

        Assert.Equal((byte)60, result[0]);       // top-left: (80 + 400) / 8
        Assert.Equal((byte)60, result[4]);       // top-right: top only
        Assert.Equal((byte)120, result[4 * 8]);  // bottom-left: left only
        Assert.Equal((byte)100, result[4 * 8 + 4]); // bottom-right: both
    }

    [Theory]
    [InlineData(true, false, 40, 128)]
    [InlineData(false, true, 80, 128)]
    public void Predict_DcWithSingleEdge_ShouldUseOnlyTheAvailableBoundary(
        bool hasTop, bool hasLeft, byte expectedTopLeft, byte expectedInterior)
    {
        byte[] top = Enumerable.Repeat((byte)40, 8).ToArray();
        byte[] left = Enumerable.Repeat((byte)80, 16).ToArray();
        byte[] result = H264ChromaIntraPrediction.Predict(
            0, 2, top, left, 0, hasTop, hasLeft, false);

        Assert.All(result.Take(4 * 8), sample => Assert.Equal(expectedTopLeft, sample));
        Assert.All(result.Skip(4 * 8), sample => Assert.Equal(expectedInterior, sample));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Predict_DcWithoutEdges_ShouldFillNeutralMidpoint(uint chromaFormat, int height)
    {
        byte[] result = H264ChromaIntraPrediction.Predict(
            0, chromaFormat, new byte[8], new byte[height], 0,
            false, false, false);

        Assert.All(result, sample => Assert.Equal((byte)128, sample));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void Predict_PlaneWithConstantReferences_ShouldRemainConstant(uint chromaFormat, int height)
    {
        byte[] top = Enumerable.Repeat((byte)90, 8).ToArray();
        byte[] left = Enumerable.Repeat((byte)90, height).ToArray();

        byte[] result = H264ChromaIntraPrediction.Predict(
            3, chromaFormat, top, left, 90, true, true, true);

        Assert.All(result, sample => Assert.Equal((byte)90, sample));
    }

    [Fact]
    public void Predict_Plane_ShouldClipToEightBitRange()
    {
        byte[] top = [255, 255, 255, 255, 255, 255, 255, 255];
        byte[] left = [0, 0, 0, 0, 0, 0, 0, 0];

        byte[] result = H264ChromaIntraPrediction.Predict(
            3, 1, top, left, 0, true, true, true);

        Assert.All(result, sample => Assert.InRange(sample, (byte)0, (byte)255));
    }

    [Fact]
    public void Predict_ShouldRejectUnavailableRequiredEdges()
    {
        byte[] top = new byte[8];
        byte[] left = new byte[16];

        Assert.Throws<FormatException>(() =>
            H264ChromaIntraPrediction.Predict(1, 1, top, left, 0, true, false, true));
        Assert.Throws<FormatException>(() =>
            H264ChromaIntraPrediction.Predict(2, 1, top, left, 0, false, true, true));
        Assert.Throws<FormatException>(() =>
            H264ChromaIntraPrediction.Predict(3, 2, top, left, 0, true, true, false));
    }

    [Fact]
    public void Predict_ShouldRejectInvalidModesFormatsAndShortEdges()
    {
        byte[] top = new byte[8];
        byte[] left = new byte[16];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264ChromaIntraPrediction.Predict(4, 1, top, left, 0, true, true, true));
        Assert.Throws<NotSupportedException>(() =>
            H264ChromaIntraPrediction.Predict(0, 3, top, left, 0, true, true, true));
        Assert.Throws<ArgumentException>(() =>
            H264ChromaIntraPrediction.Predict(0, 1, new byte[7], left, 0, true, true, true));
        Assert.Throws<ArgumentException>(() =>
            H264ChromaIntraPrediction.Predict(0, 2, top, new byte[15], 0, true, true, true));
    }
}

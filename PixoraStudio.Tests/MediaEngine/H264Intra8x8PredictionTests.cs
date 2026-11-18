using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Intra8x8PredictionTests
{
    private static readonly byte[] Top = Enumerable.Range(10, 16)
        .Select(value => (byte)value).ToArray();
    private static readonly byte[] Left = Enumerable.Range(50, 8)
        .Select(value => (byte)value).ToArray();

    [Fact]
    public void Predict_Vertical_ShouldReplicateFilteredTopAcrossRows()
    {
        byte[] result = H264Intra8x8Prediction.Predict(
            0, Top, Left, 30, true, true, true);

        Assert.Equal(64, result.Length);
        Assert.Equal((byte)((30 + 2 * 10 + 11 + 2) >> 2), result[0]);
        Assert.Equal(result.Take(8), result.Skip(56).Take(8));
    }

    [Fact]
    public void Predict_Horizontal_ShouldReplicateFilteredLeftAcrossColumns()
    {
        byte[] result = H264Intra8x8Prediction.Predict(
            1, Top, Left, 30, true, true, true);

        Assert.Equal(64, result.Length);
        for (int y = 0; y < 8; y++)
            Assert.All(result.Skip(y * 8).Take(8),
                value => Assert.Equal(result[y * 8], value));
    }

    [Theory]
    [InlineData(true, true, 33)]
    [InlineData(true, false, 16)]
    [InlineData(false, true, 54)]
    [InlineData(false, false, 128)]
    public void Predict_Dc_ShouldUseOnlyAvailableUnfilteredEdges(
        bool hasTop, bool hasLeft, byte expected)
    {
        byte[] top = Enumerable.Repeat((byte)16, 16).ToArray();
        byte[] left = Enumerable.Repeat((byte)50, 8).ToArray();
        byte[] result = H264Intra8x8Prediction.Predict(
            2, top, left, 30, hasTop, hasLeft, false);
        Assert.All(result, sample => Assert.Equal(expected, sample));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Predict_DirectionalModes_ShouldProduceCompleteEightByEightBlocks(uint mode)
    {
        byte[] result = H264Intra8x8Prediction.Predict(
            mode, Top, Left, 30, true, true, true);

        Assert.Equal(64, result.Length);
        Assert.All(result, sample => Assert.InRange(sample, (byte)0, (byte)255));
    }

    [Fact]
    public void Predict_DiagonalDownLeft_ShouldExtendUnavailableTopRightSamples()
    {
        byte[] result = H264Intra8x8Prediction.Predict(
            3, Top, Left, 30, true, true, true, hasTopRight: false);

        Assert.Equal(result[7], result[14]);
        Assert.Equal(result[15], result[22]);
    }

    [Theory]
    [InlineData(0, false, true, true)]
    [InlineData(1, true, false, true)]
    [InlineData(4, true, true, false)]
    public void Predict_ShouldRejectUnavailableRequiredReferences(
        uint mode, bool hasTop, bool hasLeft, bool hasTopLeft)
    {
        Assert.Throws<FormatException>(() => H264Intra8x8Prediction.Predict(
            mode, Top, Left, 30, hasTop, hasLeft, hasTopLeft));
    }

    [Fact]
    public void Predict_ShouldRejectInvalidModeAndShortEdges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264Intra8x8Prediction.Predict(9, Top, Left, 30, true, true, true));
        Assert.Throws<ArgumentException>(() =>
            H264Intra8x8Prediction.Predict(2, new byte[15], Left, 0, true, true, true));
    }

    [Fact]
    public void Predict_ShouldUseTopRightSamplesWhenAvailable()
    {
        byte[] topWithDifferentRight = Top.ToArray();
        topWithDifferentRight[8] = 220;
        byte[] withTopRight = H264Intra8x8Prediction.Predict(
            3, topWithDifferentRight, Left, 30, true, true, true, true);
        byte[] withoutTopRight = H264Intra8x8Prediction.Predict(
            3, topWithDifferentRight, Left, 30, true, true, true, false);

        Assert.NotEqual(withTopRight[7], withoutTopRight[7]);
    }
}

using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraPredictionTests
{
    private static readonly byte[] Top4 = [10, 20, 30, 40, 50, 60, 70, 80];
    private static readonly byte[] Left4 = [50, 60, 70, 80];

    [Fact]
    public void Predict4x4_Vertical_ShouldReplicateTopSamplesAcrossRows()
    {
        byte[] result = H264IntraPrediction.Predict4x4(0, Top4, Left4, 15, true, true, true);
        Assert.Equal(new byte[] { 10, 20, 30, 40, 10, 20, 30, 40,
            10, 20, 30, 40, 10, 20, 30, 40 }, result);
    }

    [Fact]
    public void Predict4x4_Horizontal_ShouldReplicateLeftSamplesAcrossColumns()
    {
        byte[] result = H264IntraPrediction.Predict4x4(1, Top4, Left4, 15, true, true, true);
        Assert.Equal(new byte[] { 50, 50, 50, 50, 60, 60, 60, 60,
            70, 70, 70, 70, 80, 80, 80, 80 }, result);
    }

    [Theory]
    [InlineData(true, true, 40)]
    [InlineData(true, false, 25)]
    [InlineData(false, true, 65)]
    [InlineData(false, false, 128)]
    public void Predict4x4_Dc_ShouldUseAvailableReferenceEdges(
        bool hasTop, bool hasLeft, byte expected)
    {
        byte[] result = H264IntraPrediction.Predict4x4(
            2, Top4, Left4, 15, hasTop, hasLeft, true);
        Assert.All(result, sample => Assert.Equal(expected, sample));
    }

    [Fact]
    public void Predict4x4_DirectionalModes_ShouldProduceCompleteClippedBlocks()
    {
        for (uint mode = 3; mode <= 8; mode++)
        {
            byte[] result = H264IntraPrediction.Predict4x4(
                mode, Top4, Left4, 15, true, true, true);
            Assert.Equal(16, result.Length);
            Assert.All(result, sample => Assert.InRange(sample, (byte)0, (byte)255));
        }
    }

    [Fact]
    public void Predict4x4_DiagonalDownLeft_ShouldReplicateUnavailableTopRight()
    {
        byte[] result = H264IntraPrediction.Predict4x4(
            3, Top4, Left4, 15, true, true, true,
            hasTopRight: false);
        Assert.Equal(result[3], result[6]);
        Assert.Equal(result[7], result[10]);
    }

    [Fact]
    public void Predict4x4_ShouldRejectUnavailableRequiredReferences()
    {
        Assert.Throws<FormatException>(() =>
            H264IntraPrediction.Predict4x4(0, Top4, Left4, 15, false, true, true));
        Assert.Throws<FormatException>(() =>
            H264IntraPrediction.Predict4x4(4, Top4, Left4, 15, true, true, false));
    }

    [Fact]
    public void Predict4x4_ShouldRejectInvalidModeAndShortReferences()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264IntraPrediction.Predict4x4(9, Top4, Left4, 0, true, true, true));
        Assert.Throws<ArgumentException>(() =>
            H264IntraPrediction.Predict4x4(2, new byte[3], Left4, 0, true, true, true));
    }

    [Fact]
    public void Predict16x16_VerticalAndHorizontal_ShouldReplicateReferenceEdges()
    {
        byte[] top = Enumerable.Range(0, 16).Select(i => (byte)(i + 10)).ToArray();
        byte[] left = Enumerable.Range(0, 16).Select(i => (byte)(i + 40)).ToArray();

        byte[] vertical = H264IntraPrediction.Predict16x16(0, top, left, 0, true, true);
        byte[] horizontal = H264IntraPrediction.Predict16x16(1, top, left, 0, true, true);

        Assert.Equal(top.Take(16), vertical.Take(16));
        Assert.Equal(top.Take(16), vertical.Skip(240));
        Assert.All(Enumerable.Range(0, 16), y =>
            Assert.All(horizontal.Skip(y * 16).Take(16), sample => Assert.Equal(left[y], sample)));
    }

    [Theory]
    [InlineData(true, true, 30)]
    [InlineData(true, false, 18)]
    [InlineData(false, true, 48)]
    [InlineData(false, false, 128)]
    public void Predict16x16_Dc_ShouldUseAvailableReferenceEdges(
        bool hasTop, bool hasLeft, byte expected)
    {
        byte[] top = Enumerable.Repeat((byte)10, 16).ToArray();
        byte[] left = Enumerable.Repeat((byte)50, 16).ToArray();
        byte[] result = H264IntraPrediction.Predict16x16(2, top, left, 0, hasTop, hasLeft);
        Assert.All(result, sample => Assert.Equal(expected, sample));
    }

    [Fact]
    public void Predict16x16_Plane_ShouldProduceConstantForConstantReferences()
    {
        byte[] top = Enumerable.Repeat((byte)90, 16).ToArray();
        byte[] left = Enumerable.Repeat((byte)90, 16).ToArray();
        byte[] result = H264IntraPrediction.Predict16x16(3, top, left, 90, true, true);
        Assert.All(result, sample => Assert.Equal((byte)90, sample));
    }

    [Fact]
    public void Predict16x16_ShouldRejectMissingReferencesForDirectionalModes()
    {
        byte[] samples = new byte[16];
        Assert.Throws<FormatException>(() =>
            H264IntraPrediction.Predict16x16(0, samples, samples, 0, false, true));
        Assert.Throws<FormatException>(() =>
            H264IntraPrediction.Predict16x16(3, samples, samples, 0, true, false));
    }

    [Fact]
    public void AddResidual_ShouldAddAndClipAtEightBitRange()
    {
        byte[] result = H264IntraPrediction.AddResidual(
            [0, 10, 250, 255], [-5, 5, 20, 10]);
        Assert.Equal(new byte[] { 0, 15, 255, 255 }, result);
    }

    [Fact]
    public void AddResidual_ShouldSupportHighBitDepthAndValidateInput()
    {
        byte[] result = H264IntraPrediction.AddResidual([255, 0], [100, -100], 10);
        Assert.Equal(new byte[] { 255, 0 }, result);
        Assert.Throws<ArgumentException>(() =>
            H264IntraPrediction.AddResidual([1], [1, 2]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            H264IntraPrediction.AddResidual([1], [1], 15));
    }
}

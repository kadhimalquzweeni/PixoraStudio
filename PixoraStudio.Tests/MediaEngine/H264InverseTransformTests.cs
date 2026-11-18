using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InverseTransformTests
{
    [Fact]
    public void Inverse4x4_ShouldReturnZeroBlockForZeroInput()
    {
        Assert.Equal(new int[16], H264InverseTransform.Inverse4x4(new int[16]));
    }

    [Fact]
    public void Inverse4x4_ShouldDistributeDcCoefficientAcrossBlock()
    {
        int[] input = new int[16];
        input[0] = 64;
        Assert.Equal(Enumerable.Repeat(1, 16), H264InverseTransform.Inverse4x4(input));
    }

    [Fact]
    public void InverseChromaDc2x2_ShouldApplyHadamardTransform()
    {
        Assert.Equal(new[] { 10, -2, -4, 0 }, H264InverseTransform.InverseChromaDc2x2(new[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void Inverse4x4_ShouldRejectWrongCoefficientCount()
    {
        Assert.Throws<ArgumentException>(() => H264InverseTransform.Inverse4x4(new int[15]));
    }

    [Fact]
    public void Inverse8x8_ShouldReturnZeroBlockForZeroInput()
    {
        Assert.Equal(new int[64], H264InverseTransform.Inverse8x8(new int[64]));
    }

    [Fact]
    public void Inverse8x8_ShouldDistributeDcCoefficientAcrossEntireBlock()
    {
        int[] input = new int[64];
        input[0] = 64;

        Assert.Equal(Enumerable.Repeat(1, 64), H264InverseTransform.Inverse8x8(input));
    }

    [Fact]
    public void Inverse8x8_ShouldDistributeNegativeDcCoefficientAcrossEntireBlock()
    {
        int[] input = new int[64];
        input[0] = -64;

        Assert.Equal(Enumerable.Repeat(-1, 64), H264InverseTransform.Inverse8x8(input));
    }

    [Fact]
    public void Inverse8x8_ShouldRejectWrongCoefficientCount()
    {
        Assert.Throws<ArgumentException>(() => H264InverseTransform.Inverse8x8(new int[63]));
    }
}

using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacInverseQuantizerTests
{
    private readonly AacInverseQuantizer _quantizer = new();

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(1, 1f)]
    [InlineData(-1, -1f)]
    [InlineData(8, 16f)]
    [InlineData(-8, -16f)]
    public void Dequantize_ShouldReturnExpectedValues(
        int coefficient,
        float expected)
    {
        float result = _quantizer.Dequantize(coefficient);

        Assert.Equal(expected, result, 5);
    }

    [Fact]
    public void Dequantize_ShouldPreserveSign()
    {
        float positive = _quantizer.Dequantize(5);
        float negative = _quantizer.Dequantize(-5);

        Assert.True(positive > 0);
        Assert.True(negative < 0);
        Assert.Equal(positive, Math.Abs(negative));
    }

    [Fact]
    public void Dequantize_ShouldProcessBuffer()
    {
        int[] input = [0, 1, -1, 8, -8];
        float[] output = new float[input.Length];

        _quantizer.Dequantize(input, output);

        Assert.Equal(
            [0f, 1f, -1f, 16f, -16f],
            output);
    }

    [Fact]
    public void Dequantize_ShouldRejectOutputBufferThatIsTooSmall()
    {
        int[] input = [1, 2, 3];
        float[] output = new float[2];

        Assert.Throws<ArgumentException>(
            () => _quantizer.Dequantize(input, output));
    }
}
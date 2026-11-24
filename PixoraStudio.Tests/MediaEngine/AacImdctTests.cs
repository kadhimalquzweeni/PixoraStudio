using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacImdctTests
{
    [Theory]
    [InlineData(128, 256)]
    [InlineData(1024, 2048)]
    public void Transform_ShouldReturnTwiceTheInputLength(int inputLength, int expectedLength)
    {
        float[] result = new AacImdct().Transform(new float[inputLength]);

        Assert.Equal(expectedLength, result.Length);
        Assert.All(result, sample => Assert.Equal(0f, sample));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(1024)]
    public void Transform_ShouldMatchDirectImdctDefinition(int length)
    {
        float[] input = new float[length];
        input[0] = 1.25f;
        input[3] = -0.5f;

        float[] actual = new AacImdct().Transform(input);
        int sample = 17;

        double expected = 0;
        for (int coefficient = 0; coefficient < length; coefficient++)
        {
            expected += input[coefficient] * Math.Cos(
                Math.PI / length *
                (sample + 0.5 + length / 2.0) *
                (coefficient + 0.5));
        }

        expected *= 2.0 / length;

        Assert.Equal((float)expected, actual[sample], 6);
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void Transform_ShouldRejectUnsupportedInputLength()
    {
        Assert.Throws<ArgumentException>(
            () => new AacImdct().Transform(new float[256]));
    }
}

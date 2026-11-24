using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacWindowFunctionTests
{
    [Theory]
    [InlineData(false, 2048)]
    [InlineData(true, 256)]
    public void CreateSine_ShouldReturnSymmetricWindow(bool shortWindow, int expectedLength)
    {
        float[] window = AacWindowFunction.Create(AacWindowShape.Sine, shortWindow);

        Assert.Equal(expectedLength, window.Length);
        for (int i = 0; i < window.Length / 2; i++)
            Assert.Equal(window[i], window[window.Length - 1 - i], 6);

        Assert.InRange(window[0], 0f, 0.01f);
        Assert.InRange(window[window.Length / 2], 0.99f, 1f);
    }

    [Theory]
    [InlineData(false, 2048)]
    [InlineData(true, 256)]
    public void CreateKbd_ShouldReturnFiniteSymmetricWindow(bool shortWindow, int expectedLength)
    {
        float[] window = AacWindowFunction.Create(
            AacWindowShape.KaiserBesselDerived,
            shortWindow);

        Assert.Equal(expectedLength, window.Length);
        Assert.All(window, value => Assert.True(float.IsFinite(value)));
        for (int i = 0; i < window.Length / 2; i++)
            Assert.Equal(window[i], window[window.Length - 1 - i], 5);
        Assert.All(window, value => Assert.InRange(value, 0f, 1f));
    }

    [Fact]
    public void Create_ShouldRejectUnknownShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AacWindowFunction.Create((AacWindowShape)99));
    }
}

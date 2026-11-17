using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacTnsSpectralProcessorTests
{
    private readonly AacTnsCoefficientDecoder _coefficientDecoder = new();
    private readonly AacTnsSpectralProcessor _processor = new();

    [Theory]
    [InlineData(false, false, 1, -0.20791170f)]
    [InlineData(false, true, 1, -0.43388373f)]
    [InlineData(true, false, 1, -0.20791170f)]
    [InlineData(true, true, 1, -0.43388373f)]
    public void Decode_ShouldReconstructReflectionCoefficient(
        bool compressed, bool resolution, int index, float expected)
    {
        var window = new AacTnsWindow(
            resolution,
            [new AacTnsFilter(1, 1, false, compressed, [index])]);

        float[] lpc = _coefficientDecoder.Decode(
            window, Assert.Single(window.Filters));

        Assert.Equal(expected, Assert.Single(lpc), 6);
    }

    [Fact]
    public void Decode_ShouldApplyLpcStepUpRecursion()
    {
        var window = new AacTnsWindow(
            true,
            [new AacTnsFilter(1, 2, false, false, [1, 2])]);

        float[] lpc = _coefficientDecoder.Decode(
            window, Assert.Single(window.Filters));

        float firstReflection = -0.20791170f;
        float secondReflection = -0.40673664f;
        Assert.Equal(firstReflection + (secondReflection * firstReflection), lpc[0], 6);
        Assert.Equal(secondReflection, lpc[1], 6);
    }

    [Fact]
    public void Decode_ShouldRejectCoefficientCountMismatch()
    {
        var filter = new AacTnsFilter(1, 2, false, false, [1]);

        Assert.Throws<InvalidDataException>(() =>
            _coefficientDecoder.Decode(new AacTnsWindow(true, [filter]), filter));
    }

    [Fact]
    public void Apply_ShouldFilterLongWindowInForwardDirectionWithoutMutatingInput()
    {
        int[] offsets = [0, 256, 512, 768, 1024];
        float[] spectrum = new float[1024];
        spectrum[768] = 1f;
        var filter = new AacTnsFilter(1, 1, false, false, [1]);
        var tns = new AacTnsData([new AacTnsWindow(true, [filter])]);

        float[] result = _processor.Apply(
            spectrum, tns, offsets, LongInfo());

        Assert.Equal(1f, result[768]);
        Assert.Equal(0.20791170f, result[769], 6);
        Assert.Equal(0f, spectrum[769]);
    }

    [Fact]
    public void Apply_ShouldFilterLongWindowInReverseDirection()
    {
        int[] offsets = [0, 256, 512, 768, 1024];
        float[] spectrum = new float[1024];
        spectrum[1023] = 1f;
        var filter = new AacTnsFilter(1, 1, true, false, [1]);
        var tns = new AacTnsData([new AacTnsWindow(true, [filter])]);

        float[] result = _processor.Apply(
            spectrum, tns, offsets, LongInfo());

        Assert.Equal(1f, result[1023]);
        Assert.Equal(0.20791170f, result[1022], 6);
    }

    [Fact]
    public void Apply_ShouldProcessEachOfEightShortWindowsIndependently()
    {
        int[] offsets = [0, 32, 64, 96, 128];
        float[] spectrum = new float[1024];
        var filter = new AacTnsFilter(1, 1, true, false, [1]);
        var windows = Enumerable.Range(0, 8)
            .Select(_ => new AacTnsWindow(true, [filter]))
            .ToArray();
        for (int window = 0; window < 8; window++)
            spectrum[(window * 128) + 127] = 1f;

        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence, AacWindowShape.Sine, 4, 0, false);
        float[] result = _processor.Apply(
            spectrum, new AacTnsData(windows), offsets, info);

        for (int window = 0; window < 8; window++)
        {
            Assert.Equal(1f, result[(window * 128) + 127]);
            Assert.Equal(0.20791170f, result[(window * 128) + 126], 6);
        }
    }

    [Fact]
    public void Apply_ShouldRejectIncorrectSpectrumLength()
    {
        Assert.Throws<ArgumentException>(() =>
            _processor.Apply(
                new float[128],
                new AacTnsData([new AacTnsWindow(false, [])]),
                [0, 1024],
                LongInfo()));
    }

    private static AacIndividualChannelStreamInfo LongInfo() =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, 4, 0, false);
}

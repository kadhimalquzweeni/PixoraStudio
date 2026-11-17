using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacPulseDataApplierTests
{
    private readonly AacPulseDataApplier _applier = new();

    [Fact]
    public void Apply_ShouldAdjustTargetCoefficientsAndPreserveInput()
    {
        int[] original = [5, -4, 0, 2];
        AacSpectralBandData band = new(0, 0, 1, original);
        AacPulseData pulse = new(0, [new AacPulse(0, 2), new AacPulse(1, 3), new AacPulse(1, 1)]);
        AacIndividualChannelStreamInfo info = LongInfo();

        var result = _applier.Apply([band], pulse, [0, 4, 8], info);

        Assert.Equal(new[] { 3, -1, 1, 2 }, Assert.Single(result).Coefficients);
        Assert.Equal(new[] { 5, -4, 0, 2 }, original);
        Assert.NotSame(original, result[0].Coefficients);
    }

    [Fact]
    public void Apply_ShouldRejectPulsePositionOutsideDecodedSpectrum()
    {
        var pulse = new AacPulseData(0, [new AacPulse(9, 1)]);
        var band = new AacSpectralBandData(0, 0, 1, [1, 2, 3, 4]);

        Assert.Throws<InvalidDataException>(() =>
            _applier.Apply([band], pulse, [0, 4, 8], LongInfo()));
    }

    [Fact]
    public void Apply_ShouldRejectShortWindowIcs()
    {
        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence, AacWindowShape.Sine, 1, 0, false);

        Assert.Throws<ArgumentException>(() =>
            _applier.Apply(
                [new AacSpectralBandData(0, 0, 1, [1, 2, 3, 4])],
                new AacPulseData(0, [new AacPulse(0, 1)]),
                [0, 4, 8], info));
    }

    [Fact]
    public void Apply_ShouldRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _applier.Apply(null!, new AacPulseData(0, [new AacPulse(0, 1)]), [0, 4], LongInfo()));
    }

    private static AacIndividualChannelStreamInfo LongInfo() =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, 2, 0, false);
}
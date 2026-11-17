using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacSpectralDequantizerTests
{
    private readonly AacSpectralDequantizer _dequantizer = new();

    [Fact]
    public void Dequantize_ShouldApplyUnitGainAtScalefactor100()
    {
        var result = _dequantizer.Dequantize(
            [new AacSpectralBandData(0, 0, 1, [0, 1, -1, 8])],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 1, 100)]));

        var values = Assert.Single(result).Coefficients;
        Assert.Equal(0f, values[0]);
        Assert.Equal(1f, values[1], 5);
        Assert.Equal(-1f, values[2], 5);
        Assert.Equal(16f, values[3], 4);
    }

    [Fact]
    public void Dequantize_ShouldApplyQuarterPowerScalefactorGain()
    {
        var result = _dequantizer.Dequantize(
            [new AacSpectralBandData(0, 2, 5, [1, -8])],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 2, 5, 104)]));

        var values = Assert.Single(result).Coefficients;
        Assert.Equal(2f, values[0], 5);
        Assert.Equal(-32f, values[1], 4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void Dequantize_ShouldLeaveNonSpectralCodebooksAtZero(int codebook)
    {
        var result = _dequantizer.Dequantize(
            [new AacSpectralBandData(0, 0, codebook, [3, -4])],
            new AacScaleFactorData(Array.Empty<AacScaleFactorBandValue>()));

        Assert.Equal(new float[] { 0, 0 }, Assert.Single(result).Coefficients);
    }

    [Fact]
    public void Dequantize_ShouldRejectMissingScalefactor()
    {
        Assert.Throws<InvalidDataException>(() =>
            _dequantizer.Dequantize(
                [new AacSpectralBandData(0, 0, 1, [1])],
                new AacScaleFactorData(Array.Empty<AacScaleFactorBandValue>())));
    }

    [Fact]
    public void Dequantize_ShouldRejectReservedCodebook()
    {
        Assert.Throws<InvalidDataException>(() =>
            _dequantizer.Dequantize(
                [new AacSpectralBandData(0, 0, 12, [1])],
                new AacScaleFactorData(Array.Empty<AacScaleFactorBandValue>())));
    }

    [Fact]
    public void Dequantize_ShouldRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _dequantizer.Dequantize(null!, new AacScaleFactorData([])));
    }
}
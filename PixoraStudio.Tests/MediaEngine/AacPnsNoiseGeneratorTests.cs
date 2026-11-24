using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacPnsNoiseGeneratorTests
{
    [Fact]
    public void Apply_ShouldNormaliseLongWindowNoiseAndApplyEnergyScalefactor()
    {
        var generator = new AacPnsNoiseGenerator(new SequenceNoiseSource(1, 0, 0, 1));
        var band = new AacDequantizedSpectralBandData(0, 0, 13, new float[4]);
        var scaleFactors = new AacScaleFactorData(
            [new AacScaleFactorBandValue(0, 0, 13, 104)]);

        var result = generator.Apply(
            [band], scaleFactors, AacScaleFactorBandTable.GetLongWindowOffsets(0), LongInfo());

        float[] values = Assert.Single(result).Coefficients;
        Assert.Equal(4f, values.Sum(value => value * value), 5);
        Assert.Equal(MathF.Sqrt(2f), values[0], 5);
        Assert.Equal(0f, values[1]);
        Assert.Equal(0f, values[2]);
        Assert.Equal(MathF.Sqrt(2f), values[3], 5);
        Assert.All(band.Coefficients, value => Assert.Equal(0f, value));
        Assert.NotSame(band.Coefficients, values);
    }

    [Fact]
    public void Apply_ShouldGenerateIndependentNormalisedNoiseForEachShortWindowInGroup()
    {
        var source = new SequenceNoiseSource(1, 0, 0, 1, 0, 1, 1, 0);
        var generator = new AacPnsNoiseGenerator(source);
        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence,
            AacWindowShape.Sine,
            1,
            0b101_0101,
            false);
        var band = new AacDequantizedSpectralBandData(0, 0, 13, new float[8]);

        var result = generator.Apply(
            [band],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 13, 100)]),
            AacScaleFactorBandTable.GetShortWindowOffsets(0),
            info);

        float[] values = Assert.Single(result).Coefficients;
        Assert.Equal(1f, values.Take(4).Sum(value => value * value), 5);
        Assert.Equal(1f, values.Skip(4).Sum(value => value * value), 5);
        Assert.Equal(8, source.SamplesRead);
    }

    [Fact]
    public void Apply_ShouldLeaveNonNoiseBandsUnchanged()
    {
        var source = new SequenceNoiseSource();
        var band = new AacDequantizedSpectralBandData(0, 0, 1, [1f, -2f]);

        var result = new AacPnsNoiseGenerator(source).Apply(
            [band],
            new AacScaleFactorData([]),
            AacScaleFactorBandTable.GetLongWindowOffsets(0),
            LongInfo());

        Assert.Equal(new[] { 1f, -2f }, Assert.Single(result).Coefficients);
        Assert.Equal(0, source.SamplesRead);
        Assert.NotSame(band.Coefficients, result[0].Coefficients);
    }

    [Fact]
    public void Apply_ShouldRejectMissingNoiseScalefactor()
    {
        Assert.Throws<InvalidDataException>(() =>
            new AacPnsNoiseGenerator(new SequenceNoiseSource(1, 0, 0, 1)).Apply(
                [new AacDequantizedSpectralBandData(0, 0, 13, new float[4])],
                new AacScaleFactorData([]),
                AacScaleFactorBandTable.GetLongWindowOffsets(0),
                LongInfo()));
    }

    [Fact]
    public void Apply_ShouldRejectIncorrectGroupedCoefficientCount()
    {
        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence,
            AacWindowShape.Sine,
            1,
            0b101_0101,
            false);

        Assert.Throws<InvalidDataException>(() =>
            new AacPnsNoiseGenerator(new SequenceNoiseSource(1, 0, 0, 1)).Apply(
                [new AacDequantizedSpectralBandData(0, 0, 13, new float[4])],
                new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 13, 100)]),
                AacScaleFactorBandTable.GetShortWindowOffsets(0),
                info));
    }

    [Fact]
    public void Apply_ShouldRejectInvalidNoiseSamples()
    {
        Assert.Throws<InvalidDataException>(() =>
            new AacPnsNoiseGenerator(new SequenceNoiseSource(float.NaN, 0, 0, 1)).Apply(
                [new AacDequantizedSpectralBandData(0, 0, 13, new float[4])],
                new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 13, 100)]),
                AacScaleFactorBandTable.GetLongWindowOffsets(0),
                LongInfo()));
    }

    private static AacIndividualChannelStreamInfo LongInfo() =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, 1, 0, false);

    private sealed class SequenceNoiseSource(params float[] samples) : IAacNoiseSource
    {
        private readonly float[] _samples = samples;
        private int _index;

        public int SamplesRead => _index;

        public float NextSample()
        {
            if (_index >= _samples.Length)
                throw new InvalidOperationException("The test noise sequence is exhausted.");

            return _samples[_index++];
        }
    }
}

using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacIndividualChannelStreamProcessorTests
{
    [Fact]
    public void Process_ShouldDequantizeAndReassembleLongWindowBands()
    {
        var data = CreateData(
            LongInfo(),
            [new AacSpectralBandData(0, 0, 1, [1, -1, 0, 8])],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 1, 100)]));

        float[] spectrum = new AacIndividualChannelStreamProcessor(
            new SequenceNoiseSource()).Process(data, 0);

        Assert.Equal(1024, spectrum.Length);
        Assert.Equal(1f, spectrum[0], 5);
        Assert.Equal(-1f, spectrum[1], 5);
        Assert.Equal(0f, spectrum[2], 5);
        Assert.Equal(16f, spectrum[3], 4);
        Assert.Equal(0f, spectrum[4]);
    }

    [Fact]
    public void Process_ShouldApplyPulseBeforeInverseQuantisation()
    {
        var data = CreateData(
            LongInfo(),
            [new AacSpectralBandData(0, 0, 1, [5, -4, 0, 2])],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 1, 100)]),
            new AacPulseData(0, [new AacPulse(0, 2), new AacPulse(1, 3), new AacPulse(1, 1)]));

        float[] spectrum = new AacIndividualChannelStreamProcessor(
            new SequenceNoiseSource()).Process(data, 0);

        Assert.Equal(MathF.Pow(3f, 4f / 3f), spectrum[0], 4);
        Assert.Equal(-1f, spectrum[1], 5);
        Assert.Equal(1f, spectrum[2], 5);
        Assert.Equal(MathF.Pow(2f, 4f / 3f), spectrum[3], 4);
    }

    [Fact]
    public void Process_ShouldGeneratePnsAndApplyTnsToReassembledSpectrum()
    {
        var tnsFilter = new AacTnsFilter(41, 1, false, false, [1]);
        var tns = new AacTnsData([new AacTnsWindow(true, [tnsFilter])]);
        var data = CreateData(
            LongInfo(),
            [new AacSpectralBandData(0, 0, 13, new int[4])],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 13, 100)]),
            tnsData: tns);

        float[] spectrum = new AacIndividualChannelStreamProcessor(
            new SequenceNoiseSource(1, 0, 0, 0)).Process(data, 0);

        Assert.Equal(1f, spectrum[0], 5);
        Assert.Equal(0.20791170f, spectrum[1], 5);
        Assert.Equal(0.043228f, spectrum[2], 5);
        Assert.Equal(0.008986f, spectrum[3], 5);
    }

    [Fact]
    public void Process_ShouldReassembleGroupedShortWindowBandsIntoWindowMajorSpectrum()
    {
        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence,
            AacWindowShape.Sine,
            1,
            0b101_0101,
            false);
        var data = CreateData(
            info,
            [new AacSpectralBandData(0, 0, 1, [1, 2, 3, 4, 5, 6, 7, 8])],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 1, 100)]));

        float[] spectrum = new AacIndividualChannelStreamProcessor(
            new SequenceNoiseSource()).Process(data, 0);

        float[] firstWindowExpected =
            new[] { 1f, 2f, 3f, 4f }.Select(value => MathF.Pow(value, 4f / 3f)).ToArray();
        float[] secondWindowExpected =
            new[] { 5f, 6f, 7f, 8f }.Select(value => MathF.Pow(value, 4f / 3f)).ToArray();

        for (int i = 0; i < firstWindowExpected.Length; i++)
        {
            Assert.Equal(firstWindowExpected[i], spectrum[i], 5);
            Assert.Equal(secondWindowExpected[i], spectrum[128 + i], 5);
        }
        Assert.All(spectrum.Skip(4).Take(124), value => Assert.Equal(0f, value));
        Assert.All(spectrum.Skip(132), value => Assert.Equal(0f, value));
    }

    [Fact]
    public void Process_ShouldRejectDuplicateSpectralBands()
    {
        var duplicate = new AacSpectralBandData(0, 0, 1, [1, 2, 3, 4]);
        var data = CreateData(
            LongInfo(),
            [duplicate, duplicate],
            new AacScaleFactorData([new AacScaleFactorBandValue(0, 0, 1, 100)]));

        Assert.Throws<InvalidDataException>(() =>
            new AacIndividualChannelStreamProcessor(new SequenceNoiseSource()).Process(data, 0));
    }

    private static AacIndividualChannelStreamData CreateData(
        AacIndividualChannelStreamInfo info,
        IReadOnlyList<AacSpectralBandData> spectralBands,
        AacScaleFactorData scaleFactors,
        AacPulseData? pulseData = null,
        AacTnsData? tnsData = null) =>
        new(
            100,
            info,
            Array.Empty<AacSectionInfo>(),
            scaleFactors,
            pulseData,
            tnsData ?? new AacTnsData([]),
            spectralBands);

    private static AacIndividualChannelStreamInfo LongInfo() =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, 1, 0, false);

    private sealed class SequenceNoiseSource(params float[] samples) : IAacNoiseSource
    {
        private readonly float[] _samples = samples;
        private int _index;

        public float NextSample()
        {
            if (_index >= _samples.Length)
                throw new InvalidOperationException("The test noise sequence is exhausted.");

            return _samples[_index++];
        }
    }
}

using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacPcmSynthesisProcessorTests
{
    [Theory]
    [InlineData(AacWindowSequence.OnlyLongSequence)]
    [InlineData(AacWindowSequence.LongStartSequence)]
    [InlineData(AacWindowSequence.EightShortSequence)]
    [InlineData(AacWindowSequence.LongStopSequence)]
    public void Process_ShouldReturnMonoPcmFrameForEveryWindowSequence(
        AacWindowSequence sequence)
    {
        AacPcmSynthesisProcessor processor = new();
        AacIndividualChannelStreamInfo info = CreateInfo(sequence);

        PcmAudioBuffer result = processor.Process(
            new float[1024],
            info,
            44100);

        Assert.Equal(44100, result.Format.SampleRate);
        Assert.Equal(1, result.Format.ChannelCount);
        Assert.Equal(1024, result.FrameCount);
        Assert.All(result.Samples.ToArray(), sample => Assert.Equal(0f, sample));
    }

    [Theory]
    [InlineData(AacWindowSequence.OnlyLongSequence)]
    [InlineData(AacWindowSequence.LongStartSequence)]
    [InlineData(AacWindowSequence.EightShortSequence)]
    [InlineData(AacWindowSequence.LongStopSequence)]
    public void Process_ShouldProduceFiniteSamplesForEveryWindowSequence(
        AacWindowSequence sequence)
    {
        AacPcmSynthesisProcessor processor = new();
        float[] spectrum = new float[1024];
        spectrum[0] = 1f;
        spectrum[17] = -0.25f;

        PcmAudioBuffer result = processor.Process(
            spectrum,
            CreateInfo(sequence),
            48000);

        Assert.Equal(1024, result.FrameCount);
        Assert.All(result.Samples.ToArray(),
            sample => Assert.True(float.IsFinite(sample)));
    }

    [Fact]
    public void Process_ShouldRejectInvalidSampleRate()
    {
        AacPcmSynthesisProcessor processor = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            processor.Process(
                new float[1024],
                CreateInfo(AacWindowSequence.OnlyLongSequence),
                0));
    }

    [Fact]
    public void Process_ShouldRejectInvalidSpectrumLength()
    {
        AacPcmSynthesisProcessor processor = new();

        Assert.Throws<ArgumentException>(() =>
            processor.Process(
                new float[128],
                CreateInfo(AacWindowSequence.OnlyLongSequence),
                44100));
    }

    [Fact]
    public void Reset_ShouldClearOverlapAndPreviousWindowState()
    {
        AacPcmSynthesisProcessor processor = new();
        float[] spectrum = new float[1024];
        spectrum[0] = 1f;

        processor.Process(
            spectrum,
            CreateInfo(AacWindowSequence.OnlyLongSequence, AacWindowShape.KaiserBesselDerived),
            44100);
        processor.Reset();

        PcmAudioBuffer afterReset = processor.Process(
            new float[1024],
            CreateInfo(AacWindowSequence.OnlyLongSequence),
            44100);

        Assert.All(afterReset.Samples.ToArray(),
            sample => Assert.Equal(0f, sample));
    }

    private static AacIndividualChannelStreamInfo CreateInfo(
        AacWindowSequence sequence,
        AacWindowShape shape = AacWindowShape.Sine) =>
        new(sequence, shape, 0, 0, false);
}

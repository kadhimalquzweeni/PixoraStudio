using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacPcmSynthesisProcessorTests
{
    [Fact]
    public void Process_ShouldReturnMonoPcmFrameForLongWindow()
    {
        AacPcmSynthesisProcessor processor = new();
        AacIndividualChannelStreamInfo info = new(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine,
            0,
            0,
            false);

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
    [InlineData(AacWindowSequence.LongStartSequence)]
    [InlineData(AacWindowSequence.EightShortSequence)]
    [InlineData(AacWindowSequence.LongStopSequence)]
    public void Process_ShouldRejectUnsupportedWindowTransitions(AacWindowSequence sequence)
    {
        AacPcmSynthesisProcessor processor = new();
        AacIndividualChannelStreamInfo info = new(
            sequence,
            AacWindowShape.Sine,
            0,
            0,
            false);

        Assert.Throws<NotSupportedException>(() =>
            processor.Process(new float[1024], info, 44100));
    }

    [Fact]
    public void Process_ShouldRejectInvalidSampleRate()
    {
        AacPcmSynthesisProcessor processor = new();
        AacIndividualChannelStreamInfo info = new(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine,
            0,
            0,
            false);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            processor.Process(new float[1024], info, 0));
    }

    [Fact]
    public void Process_ShouldRejectInvalidSpectrumLength()
    {
        AacPcmSynthesisProcessor processor = new();
        AacIndividualChannelStreamInfo info = new(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine,
            0,
            0,
            false);

        Assert.Throws<ArgumentException>(() =>
            processor.Process(new float[128], info, 44100));
    }
}

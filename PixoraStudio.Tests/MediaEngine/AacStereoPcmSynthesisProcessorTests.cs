using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacStereoPcmSynthesisProcessorTests
{
    [Fact]
    public void Process_ShouldReturnInterleavedStereoPcm()
    {
        AacStereoPcmSynthesisProcessor processor = new();
        AacIndividualChannelStreamInfo info = new(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine,
            0, 0, false);

        PcmAudioBuffer result = processor.Process(
            new float[1024], info,
            new float[1024], info,
            48000);

        Assert.Equal(48000, result.Format.SampleRate);
        Assert.Equal(2, result.Format.ChannelCount);
        Assert.Equal(1024, result.FrameCount);
        Assert.Equal(2048, result.SampleCount);
        Assert.All(result.Samples.ToArray(), value => Assert.Equal(0f, value));
    }

    [Fact]
    public void Reset_ShouldResetBothChannelOverlapStates()
    {
        AacStereoPcmSynthesisProcessor processor = new();
        AacIndividualChannelStreamInfo info = new(
            AacWindowSequence.OnlyLongSequence,
            AacWindowShape.Sine,
            0, 0, false);
        float[] spectrum = new float[1024];
        spectrum[0] = 1f;

        processor.Process(spectrum, info, spectrum, info, 44100);
        processor.Reset();

        PcmAudioBuffer result = processor.Process(
            new float[1024], info,
            new float[1024], info,
            44100);

        Assert.All(result.Samples.ToArray(), value => Assert.Equal(0f, value));
    }
}

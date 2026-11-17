
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class PcmAudioBufferTests
{
    [Fact]
    public void Constructor_ShouldStoreInterleavedSamples()
    {
        PcmAudioFormat format = new(44100, 2);

        float[] samples =
        [
            0.1f, 0.2f,
            0.3f, 0.4f,
            0.5f, 0.6f
        ];

        PcmAudioBuffer buffer = new(format, samples);

        Assert.Equal(6, buffer.SampleCount);
        Assert.Equal(3, buffer.FrameCount);
        Assert.Equal(samples, buffer.Samples.ToArray());
    }

    [Fact]
    public void Constructor_ShouldCopyInputSamples()
    {
        PcmAudioFormat format = new(44100, 2);
        float[] samples = [0.1f, 0.2f];

        PcmAudioBuffer buffer = new(format, samples);

        samples[0] = 0.9f;

        Assert.Equal(0.1f, buffer.GetSample(0, 0));
    }

    [Fact]
    public void Constructor_ShouldRejectNullFormat()
    {
        Assert.Throws<ArgumentNullException>(
            () => new PcmAudioBuffer(null!, new float[] { 0.1f }));
    }

    [Fact]
    public void Constructor_ShouldRejectIncompleteFrame()
    {
        PcmAudioFormat format = new(44100, 2);

        Assert.Throws<ArgumentException>(
            () => new PcmAudioBuffer(format, new float[] { 0.1f, 0.2f, 0.3f }));
    }

    [Fact]
    public void Constructor_ShouldAllowEmptyBuffer()
    {
        PcmAudioFormat format = new(44100, 2);

        PcmAudioBuffer buffer = new(format, []);

        Assert.Equal(0, buffer.FrameCount);
        Assert.Equal(0, buffer.SampleCount);
        Assert.Equal(0, buffer.DurationSeconds);
    }

    [Fact]
    public void GetSample_ShouldReturnCorrectChannelSample()
    {
        PcmAudioFormat format = new(44100, 2);

        PcmAudioBuffer buffer = new(
            format,
            new float[]
            {
                0.1f, 0.2f,
                0.3f, 0.4f
            });

        Assert.Equal(0.1f, buffer.GetSample(0, 0));
        Assert.Equal(0.2f, buffer.GetSample(0, 1));
        Assert.Equal(0.3f, buffer.GetSample(1, 0));
        Assert.Equal(0.4f, buffer.GetSample(1, 1));
    }

    [Fact]
    public void GetSample_ShouldRejectInvalidFrameIndex()
    {
        PcmAudioFormat format = new(44100, 2);

        PcmAudioBuffer buffer = new(format, new float[] { 0.1f, 0.2f });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => buffer.GetSample(1, 0));
    }

    [Fact]
    public void GetSample_ShouldRejectInvalidChannelIndex()
    {
        PcmAudioFormat format = new(44100, 2);

        PcmAudioBuffer buffer = new(format, new float[] { 0.1f, 0.2f });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => buffer.GetSample(0, 2));
    }

    [Fact]
    public void ToArray_ShouldReturnIndependentCopy()
    {
        PcmAudioFormat format = new(44100, 2);

        PcmAudioBuffer buffer = new(format, new float[] { 0.1f, 0.2f });

        float[] copy = buffer.ToArray();
        copy[0] = 0.9f;

        Assert.Equal(0.1f, buffer.GetSample(0, 0));
    }

    [Fact]
    public void DurationSeconds_ShouldUseFrameCountAndSampleRate()
    {
        PcmAudioFormat format = new(48000, 2);

        PcmAudioBuffer buffer = new(
            format,
            new float[48000 * 2]);

        Assert.Equal(1.0, buffer.DurationSeconds);
    }
}


using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class PcmAudioFormatTests
{
    [Fact]
    public void Constructor_ShouldCreateValidFormat()
    {
        PcmAudioFormat format = new(44100, 2);

        Assert.Equal(44100, format.SampleRate);
        Assert.Equal(2, format.ChannelCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldRejectInvalidSampleRate(int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcmAudioFormat(sampleRate, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(33)]
    public void Constructor_ShouldRejectInvalidChannelCount(int channels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcmAudioFormat(44100, channels));
    }

    [Theory]
    [InlineData(44100, 44100, 1.0)]
    [InlineData(48000, 24000, 0.5)]
    [InlineData(96000, 0, 0.0)]
    public void GetDurationSeconds_ShouldCalculateDuration(
        int sampleRate,
        int frameCount,
        double expected)
    {
        PcmAudioFormat format = new(sampleRate, 2);

        Assert.Equal(expected, format.GetDurationSeconds(frameCount));
    }

    [Fact]
    public void GetDurationSeconds_ShouldRejectNegativeFrameCount()
    {
        PcmAudioFormat format = new(44100, 2);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => format.GetDurationSeconds(-1));
    }
}

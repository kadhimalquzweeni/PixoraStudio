using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class AudioOutputSettingsTests
{
    [Theory]
    [InlineData(0d, 0f, 0)]
    [InlineData(25d, 0.25f, 25)]
    [InlineData(50d, 0.5f, 50)]
    [InlineData(100d, 1f, 100)]
    public void Create_ShouldConvertVolumePercentForBothOutputs(
        double volumePercent,
        float expectedPcmVolume,
        int expectedLibVlcVolume)
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            volumePercent,
            isMuted: false,
            usesCustomAacAudio: false);

        Assert.Equal(expectedPcmVolume, settings.PcmVolume);
        Assert.Equal(expectedLibVlcVolume, settings.LibVlcVolume);
        Assert.False(settings.IsMuted);
        Assert.False(settings.LibVlcMuted);
    }

    [Fact]
    public void Create_ShouldMuteBothOutputsWhenMuted()
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            75d,
            isMuted: true,
            usesCustomAacAudio: false);

        Assert.Equal(0.75f, settings.PcmVolume);
        Assert.Equal(75, settings.LibVlcVolume);
        Assert.True(settings.IsMuted);
        Assert.True(settings.LibVlcMuted);
    }

    [Fact]
    public void Create_ShouldLeaveLibVlcAudioEnabledWhenCustomAacIsDisabled()
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            80d,
            isMuted: false,
            usesCustomAacAudio: false);

        Assert.Equal(0.8f, settings.PcmVolume);
        Assert.Equal(80, settings.LibVlcVolume);
        Assert.False(settings.IsMuted);
        Assert.False(settings.LibVlcMuted);
    }

    [Fact]
    public void Create_ShouldKeepLibVlcMutedWhenCustomAacIsActive()
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            60d,
            isMuted: false,
            usesCustomAacAudio: true);

        Assert.Equal(0.6f, settings.PcmVolume);
        Assert.Equal(60, settings.LibVlcVolume);
        Assert.False(settings.IsMuted);
        Assert.True(settings.LibVlcMuted);
    }

    [Theory]
    [InlineData(-10d, 0f, 0)]
    [InlineData(150d, 1f, 100)]
    public void Create_ShouldClampOutOfRangeVolume(
        double volumePercent,
        float expectedPcmVolume,
        int expectedLibVlcVolume)
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            volumePercent,
            isMuted: false,
            usesCustomAacAudio: false);

        Assert.Equal(expectedPcmVolume, settings.PcmVolume);
        Assert.Equal(expectedLibVlcVolume, settings.LibVlcVolume);
    }

    [Fact]
    public void Create_ShouldTreatNonFiniteVolumeAsZero()
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            double.NaN,
            isMuted: false,
            usesCustomAacAudio: false);

        Assert.Equal(0f, settings.PcmVolume);
        Assert.Equal(0, settings.LibVlcVolume);
    }

    [Fact]
    public void Create_ShouldRepresentZeroVolumeAsSilentOnBothOutputs()
    {
        AudioOutputSettings settings = AudioOutputSettings.Create(
            0d,
            isMuted: false,
            usesCustomAacAudio: false);

        Assert.Equal(0f, settings.PcmVolume);
        Assert.Equal(0, settings.LibVlcVolume);
    }
}

using PixoraStudio.MediaEngine.Decoders.Audio;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AudioVideoSynchronizationPolicyTests
{
    [Fact]
    public void RequiresCorrection_DriftBelowThreshold_ShouldNotCorrect()
    {
        Assert.False(AudioVideoSynchronizationPolicy.RequiresCorrection(
            TimeSpan.FromMilliseconds(200),
            TimeSpan.Zero));
    }

    [Fact]
    public void RequiresCorrection_DriftAtThreshold_ShouldCorrect()
    {
        Assert.True(AudioVideoSynchronizationPolicy.RequiresCorrection(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.Zero));
    }

    [Fact]
    public void RequiresCorrection_AudioBehindVideo_ShouldCorrect()
    {
        Assert.True(AudioVideoSynchronizationPolicy.RequiresCorrection(
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void RequiresCorrection_AudioAheadVideo_ShouldCorrect()
    {
        Assert.True(AudioVideoSynchronizationPolicy.RequiresCorrection(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(400)));
    }

    [Fact]
    public void RequiresCorrection_CustomThreshold_ShouldBeRespected()
    {
        Assert.False(AudioVideoSynchronizationPolicy.RequiresCorrection(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void RequiresCorrection_ExtremeTimeValues_ShouldNotOverflow()
    {
        Assert.True(AudioVideoSynchronizationPolicy.RequiresCorrection(
            TimeSpan.MinValue,
            TimeSpan.MaxValue));
    }

    [Fact]
    public void RequiresCorrection_NegativeThreshold_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioVideoSynchronizationPolicy.RequiresCorrection(
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(-1)));
    }
}

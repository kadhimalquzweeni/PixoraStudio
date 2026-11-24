using PixoraStudio.Core.Models;

namespace PixoraStudio.Tests.Core;

public sealed class PlaybackScrubStateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Complete_ShouldReturnPlaybackStateCapturedAtBegin(bool wasPlaying)
    {
        PlaybackScrubState state = new();

        state.Begin(wasPlaying);

        Assert.True(state.IsActive);
        Assert.Equal(wasPlaying, state.Complete());
        Assert.False(state.IsActive);
    }

    [Fact]
    public void BeginAgain_ShouldReplacePreviousPlaybackState()
    {
        PlaybackScrubState state = new();

        state.Begin(true);
        state.Begin(false);

        Assert.False(state.Complete());
    }

    [Fact]
    public void Cancel_ShouldClearActiveScrub()
    {
        PlaybackScrubState state = new();
        state.Begin(true);

        state.Cancel();

        Assert.False(state.IsActive);
        Assert.False(state.Complete());
    }

    [Fact]
    public void Complete_WhenNotActive_ShouldReturnFalse()
    {
        PlaybackScrubState state = new();

        Assert.False(state.Complete());
    }
}

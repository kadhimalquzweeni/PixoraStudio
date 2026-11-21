namespace PixoraStudio.Core.Models;

/// <summary>
/// Captures playback state for one timeline interaction so committing a seek
/// can restore the state that existed when the user began scrubbing.
/// </summary>
public sealed class PlaybackScrubState
{
    public bool IsActive { get; private set; }

    private bool _wasPlaying;

    public void Begin(bool wasPlaying)
    {
        _wasPlaying = wasPlaying;
        IsActive = true;
    }

    /// <summary>Completes the interaction and returns its original playback state.</summary>
    public bool Complete()
    {
        bool wasPlaying = _wasPlaying;
        Reset();
        return wasPlaying;
    }

    public void Cancel() => Reset();

    private void Reset()
    {
        IsActive = false;
        _wasPlaying = false;
    }
}

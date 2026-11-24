namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>Supplies finite pseudo-random samples for AAC perceptual-noise substitution.</summary>
public interface IAacNoiseSource
{
    /// <summary>Returns a sample in the range [-1, 1].</summary>
    float NextSample();
}

/// <summary>
/// Small deterministic pseudo-random source. A decoder can retain one instance
/// across frames so noise does not repeat at every frame boundary.
/// </summary>
public sealed class AacLcgNoiseSource : IAacNoiseSource
{
    private uint _state;

    public AacLcgNoiseSource(uint seed = 0xA5A5_1F3Du)
    {
        if (seed == 0)
            throw new ArgumentOutOfRangeException(nameof(seed), "The noise seed must be non-zero.");

        _state = seed;
    }

    public float NextSample()
    {
        _state = unchecked((_state * 1_664_525u) + 1_013_904_223u);
        return ((_state >> 8) / 16_777_215f * 2f) - 1f;
    }
}

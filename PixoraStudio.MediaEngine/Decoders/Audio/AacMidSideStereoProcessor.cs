namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Applies AAC mid/side reconstruction to the spectral lines selected by
/// the channel-pair ms_used mask.
/// </summary>
public sealed class AacMidSideStereoProcessor
{
    public void Apply(
        Span<float> leftSpectrum,
        Span<float> rightSpectrum,
        ReadOnlySpan<bool> msUsed)
    {
        if (leftSpectrum.Length != rightSpectrum.Length)
            throw new ArgumentException("Left and right spectra must have equal lengths.");
        if (msUsed.Length != leftSpectrum.Length)
            throw new ArgumentException("The MS mask must contain one value per spectral line.");

        for (int i = 0; i < leftSpectrum.Length; i++)
        {
            if (!msUsed[i])
                continue;

            float mid = leftSpectrum[i];
            float side = rightSpectrum[i];
            leftSpectrum[i] = mid + side;
            rightSpectrum[i] = mid - side;
        }
    }
}

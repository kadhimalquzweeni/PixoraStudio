namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Holds the decoded per-band scalefactor, noise energy, or intensity position.
/// The value's meaning is determined by <see cref="CodebookIndex"/>.
/// </summary>
public sealed record AacScaleFactorBandValue(
    int WindowGroup,
    int ScaleFactorBand,
    int CodebookIndex,
    int Value);

/// <summary>
/// Decoded scale-factor data in AAC group-major, scalefactor-band order.
/// </summary>
public sealed class AacScaleFactorData
{
    private readonly AacScaleFactorBandValue[] _bands;

    public AacScaleFactorData(IEnumerable<AacScaleFactorBandValue> bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        _bands = bands.ToArray();
        Bands = Array.AsReadOnly(_bands);
    }

    public IReadOnlyList<AacScaleFactorBandValue> Bands { get; }
}

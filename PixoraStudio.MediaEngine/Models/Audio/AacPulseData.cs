namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>Describes one pulse adjustment in AAC pulse_data().</summary>
public sealed record AacPulse(int Offset, int Amplitude);

/// <summary>Pulse data associated with one long-window AAC ICS.</summary>
public sealed class AacPulseData
{
    public AacPulseData(int pulseStartScaleFactorBand, IEnumerable<AacPulse> pulses)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pulseStartScaleFactorBand);
        ArgumentNullException.ThrowIfNull(pulses);
        PulseStartScaleFactorBand = pulseStartScaleFactorBand;
        Pulses = Array.AsReadOnly(pulses.ToArray());
        if (Pulses.Count is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(pulses), "AAC pulse data must contain one to four pulses.");
    }

    public int PulseStartScaleFactorBand { get; }
    public IReadOnlyList<AacPulse> Pulses { get; }
}

namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>One TNS filter parsed from an AAC ICS.</summary>
public sealed record AacTnsFilter(
    int Length,
    int Order,
    bool IsDirectionReversed,
    bool CoefficientsAreCompressed,
    IReadOnlyList<int> Coefficients);

/// <summary>One window's temporal-noise-shaping filter configuration.</summary>
public sealed record AacTnsWindow(
    bool CoefficientResolution,
    IReadOnlyList<AacTnsFilter> Filters);

/// <summary>Temporal-noise-shaping data for an AAC ICS.</summary>
public sealed class AacTnsData
{
    public AacTnsData(IEnumerable<AacTnsWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        Windows = Array.AsReadOnly(windows.ToArray());
    }

    public IReadOnlyList<AacTnsWindow> Windows { get; }
}

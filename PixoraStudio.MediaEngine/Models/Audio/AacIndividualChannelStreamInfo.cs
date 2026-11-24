
namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Contains the ICS information that describes AAC spectral window organisation.
/// </summary>
public sealed record AacIndividualChannelStreamInfo(
    AacWindowSequence WindowSequence,
    AacWindowShape WindowShape,
    byte MaxScaleFactorBands,
    byte ScaleFactorGrouping,
    bool PredictorDataPresent)
{
    public bool UsesShortWindows =>
        WindowSequence == AacWindowSequence.EightShortSequence;

    /// <summary>
    /// Gets the number of groups used by the eight short windows.
    /// Returns one for long-window sequences.
    /// </summary>
    public int WindowGroupCount => WindowGroupLengths.Count;

    /// <summary>
    /// Gets the number of short windows in each group, in transmission order.
    /// Long-window sequences contain one group with one window.
    /// </summary>
    public IReadOnlyList<int> WindowGroupLengths
    {
        get
        {
            if (!UsesShortWindows)
                return Array.AsReadOnly(new[] { 1 });

            List<int> lengths = [1];

            // The most-significant grouping bit describes windows 0 and 1;
            // the least-significant bit describes windows 6 and 7.
            for (int boundary = 0; boundary < 7; boundary++)
            {
                bool joinsPrevious =
                    (ScaleFactorGrouping & (1 << (6 - boundary))) != 0;

                if (joinsPrevious)
                    lengths[^1]++;
                else
                    lengths.Add(1);
            }

            return lengths.AsReadOnly();
        }
    }
}

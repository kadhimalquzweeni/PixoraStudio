
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
    public int WindowGroupCount
    {
        get
        {
            if (!UsesShortWindows)
            {
                return 1;
            }

            int groupCount = 1;

            for (int bit = 0; bit < 7; bit++)
            {
                if ((ScaleFactorGrouping & (1 << bit)) == 0)
                {
                    groupCount++;
                }
            }

            return groupCount;
        }
    }
}

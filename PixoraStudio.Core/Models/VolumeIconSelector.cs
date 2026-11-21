namespace PixoraStudio.Core.Models;

/// <summary>
/// Selects the Lucide volume icon that best represents the current audio state.
/// The returned key maps to a Geometry resource in the WPF icon dictionary.
/// </summary>
public static class VolumeIconSelector
{
    public static string GetResourceKey(double volumePercent, bool isMuted)
    {
        if (isMuted)
            return "VolumeXIconGeometry";

        if (!double.IsFinite(volumePercent) || volumePercent <= 0d)
            return "VolumeOffIconGeometry";

        if (volumePercent < 20d)
            return "VolumeIconGeometry";

        if (volumePercent < 60d)
            return "Volume1IconGeometry";

        return "Volume2IconGeometry";
    }
}

namespace PixoraStudio.Core.Models;

/// <summary>
/// Determines whether the current trim selection can be exported.
/// Thumbnail generation is deliberately not part of this decision: it is
/// optional presentation work and does not affect MP4 export readiness.
/// </summary>
public static class VideoExportEligibility
{
    public static bool CanExport(
        string? videoPath,
        bool isExporting,
        double trimStartMilliseconds,
        double trimEndMilliseconds)
    {
        if (isExporting || string.IsNullOrWhiteSpace(videoPath))
            return false;

        if (!string.Equals(
                Path.GetExtension(videoPath),
                ".mp4",
                StringComparison.OrdinalIgnoreCase))
            return false;

        return double.IsFinite(trimStartMilliseconds) &&
            double.IsFinite(trimEndMilliseconds) &&
            trimStartMilliseconds >= 0 &&
            trimEndMilliseconds > trimStartMilliseconds;
    }
}

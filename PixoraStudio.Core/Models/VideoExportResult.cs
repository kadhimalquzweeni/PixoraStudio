namespace PixoraStudio.Core.Models;

public sealed record VideoExportResult(
    string OutputPath,
    double RequestedStartSeconds,
    double RequestedEndSeconds,
    double EffectiveStartSeconds,
    double EffectiveEndSeconds,
    long OutputSizeBytes)
{
    public double EffectiveDurationSeconds =>
        Math.Max(0, EffectiveEndSeconds - EffectiveStartSeconds);
}

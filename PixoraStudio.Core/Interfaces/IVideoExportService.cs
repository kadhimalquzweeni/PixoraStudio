using PixoraStudio.Core.Models;

namespace PixoraStudio.Core.Interfaces;

public interface IVideoExportService
{
    Task<VideoExportResult> ExportTrimmedVideoAsync(
        string sourcePath,
        string outputPath,
        double startSeconds,
        double endSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

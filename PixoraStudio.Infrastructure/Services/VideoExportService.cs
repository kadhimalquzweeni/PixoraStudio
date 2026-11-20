using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Export;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Exports trimmed MP4 files through PixoraStudio's managed MP4 remuxer.
/// Writes to a temporary file and only replaces the destination after validation.
/// </summary>
public sealed class VideoExportService : IVideoExportService
{
    private readonly Mp4TrimExportFileWriter _exportWriter;

    public VideoExportService()
        : this(new Mp4TrimExportFileWriter())
    {
    }

    public VideoExportService(Mp4TrimExportFileWriter exportWriter)
    {
        ArgumentNullException.ThrowIfNull(exportWriter);
        _exportWriter = exportWriter;
    }

    public async Task<VideoExportResult> ExportTrimmedVideoAsync(
        string sourcePath,
        string outputPath,
        double startSeconds,
        double endSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string sourceFullPath = Path.GetFullPath(sourcePath);
        string outputFullPath = Path.GetFullPath(outputPath);

        if (!File.Exists(sourceFullPath))
            throw new FileNotFoundException("The source video file does not exist.", sourceFullPath);

        if (!string.Equals(Path.GetExtension(sourceFullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Managed trim export currently accepts MP4 source files only.", nameof(sourcePath));

        if (string.Equals(sourceFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The export destination must be different from the source video.", nameof(outputPath));

        if (!string.Equals(Path.GetExtension(outputFullPath), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Managed trim export requires an .mp4 destination.", nameof(outputPath));

        string? outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("The export destination must include a directory.", nameof(outputPath));

        Directory.CreateDirectory(outputDirectory);

        string temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileNameWithoutExtension(outputFullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            Mp4TrimExportPlan plan;

            await using (FileStream source = new(
                sourceFullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            await using (FileStream destination = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                plan = await Task.Run(
                    () => _exportWriter.Export(
                        source,
                        destination,
                        startSeconds,
                        endSeconds,
                        cancellationToken,
                        progress),
                    cancellationToken);

                await destination.FlushAsync(cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputFullPath, overwrite: true);

            return new VideoExportResult(
                outputFullPath,
                plan.RequestedStartSeconds,
                plan.RequestedEndSeconds,
                plan.EffectiveStartSeconds,
                plan.EffectiveEndSeconds,
                new FileInfo(outputFullPath).Length);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Preserve the original export exception; temporary-file cleanup is best effort.
            }

            throw;
        }
    }
}

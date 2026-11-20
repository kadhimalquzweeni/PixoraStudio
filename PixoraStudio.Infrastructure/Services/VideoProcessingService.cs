using PixoraStudio.Core.Interfaces;
using PixoraStudio.Core.Models;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;
using PixoraStudio.MediaEngine.Readers.Mp4;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Reads MP4 metadata and creates thumbnails using PixoraStudio's managed
/// MP4 reader and H.264 decoder.
/// </summary>
public sealed class VideoProcessingService : IVideoProcessingService
{
    private const int MaximumThumbnailParallelism = 4;
    private const string WorkerCountEnvironmentVariable = "PIXORA_H264_THUMBNAIL_WORKERS";
    private const string CabacProfilingEnvironmentVariable = "PIXORA_H264_CABAC_PROFILE";

    public Task<VideoInfo> GetVideoInfoAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);

        return Task.Run(() =>
        {
            using FileStream stream = new(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var mediaInfo = new Mp4FileReader().Read(stream);
            return Mp4VideoInfoMapper.Map(mediaInfo, filePath);
        });
    }

    public async Task<string> GenerateThumbnailAsync(
        string filePath,
        TimeSpan position,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        await GenerateThumbnailsAsync(
            filePath, [position], [outputPath]).ConfigureAwait(false);
        return Path.GetFullPath(outputPath);
    }

    /// <summary>
    /// Parses MP4 metadata and builds the track's seek index once. Thumbnail
    /// workers then reuse precomputed seek plans and independent decoder state.
    /// </summary>
    public Task GenerateThumbnailsAsync(
        string filePath,
        IReadOnlyList<TimeSpan> positions,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(outputPaths);

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Video file not found.", filePath);
        if (positions.Count != outputPaths.Count)
            throw new ArgumentException(
                "The number of output paths must match the number of thumbnail positions.",
                nameof(outputPaths));

        for (int i = 0; i < positions.Count; i++)
        {
            if (positions[i] < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(positions));
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPaths[i]);
            if (i > 0 && positions[i] < positions[i - 1])
                throw new ArgumentException(
                    "Thumbnail positions must be in ascending order.", nameof(positions));
        }

        if (positions.Count == 0)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            Mp4MediaInfo mediaInfo;
            using (FileStream metadataStream = new(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                mediaInfo = new Mp4FileReader().Read(metadataStream);
            }

            Mp4MediaTrack videoTrack = mediaInfo
                .GetTracks(Mp4HandlerType.Video)
                .FirstOrDefault()
                ?? throw new NotSupportedException(
                    "The MP4 file does not contain a video track.");

            // Plan every requested seek before starting parallel decoding.
            // The planner validates and indexes sample timing only once.
            H264Mp4VideoSeekPlanner seekPlanner = new();
            H264Mp4VideoSeekPlan[] seekPlans = new H264Mp4VideoSeekPlan[positions.Count];
            for (int index = 0; index < positions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                seekPlans[index] = seekPlanner.Plan(
                    videoTrack, positions[index].TotalSeconds);
            }

            int? requestedWorkerCount = GetRequestedWorkerCount();
            int workerCount = GetThumbnailWorkerCount(
                positions.Count, Environment.ProcessorCount, requestedWorkerCount);
            bool enableCabacProfiling = IsCabacProfilingEnabled();
            Debug.WriteLine(
                $"[Thumbnails] Prepared {seekPlans.Length} seek plans; " +
                $"workers={workerCount}; processors={Environment.ProcessorCount}; " +
                $"cabacProfiling={enableCabacProfiling}.");

            Stopwatch batchStopwatch = Stopwatch.StartNew();
            Parallel.For(
                0,
                positions.Count,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = workerCount
                },
                index =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Stopwatch totalStopwatch = Stopwatch.StartNew();
                    int workerThreadId = Environment.CurrentManagedThreadId;
                    Debug.WriteLine(
                        $"[Thumbnail {index}] START thread={workerThreadId} " +
                        $"target={positions[index]:c} " +
                        $"firstSample={seekPlans[index].FirstSampleToDecode}");

                    using FileStream stream = new(
                        filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                    H264CabacPerformanceProfiler? cabacProfiler =
                        enableCabacProfiling ? new H264CabacPerformanceProfiler() : null;
                    H264Mp4TrackDecoder trackDecoder = new(cabacProfiler);

                    Stopwatch decodeStopwatch = Stopwatch.StartNew();
                    H264Mp4DecodedVideoFrame? frame =
                        trackDecoder.DecodeFromPlan(
                            stream,
                            mediaInfo,
                            videoTrack.TrackId,
                            seekPlans[index],
                            cancellationToken: cancellationToken)
                        .FirstOrDefault();
                    decodeStopwatch.Stop();

                    if (cabacProfiler is not null)
                    {
                        H264CabacPerformanceSnapshot snapshot = cabacProfiler.GetSnapshot();
                        Debug.WriteLine(
                            $"[H264 CABAC profile] thumbnail={index}; " +
                            $"decisionBins={snapshot.DecisionBinCount}; " +
                            $"decisionElapsed={snapshot.DecisionElapsed}; " +
                            $"bypassBins={snapshot.BypassBinCount}; " +
                            $"bypassElapsed={snapshot.BypassElapsed}; " +
                            $"renormalizations={snapshot.RenormalizationCount}; " +
                            $"renormalizationElapsed={snapshot.RenormalizationElapsed} " +
                            "(included in decisionElapsed).");
                    }

                    if (frame is null)
                    {
                        Debug.WriteLine(
                            $"[Thumbnail {index}] NO FRAME; " +
                            $"decode={decodeStopwatch.Elapsed}; total={totalStopwatch.Elapsed}.");
                        return;
                    }

                    Debug.WriteLine(
                        $"[Thumbnail {index}] DECODED thread={workerThreadId} " +
                        $"decode={decodeStopwatch.Elapsed}.");

                    try
                    {
                        Stopwatch imageStopwatch = Stopwatch.StartNew();
                        SaveThumbnail(
                            frame,
                            outputPaths[index],
                            new H264AvcParameterSetCollectionReader(),
                            new H264YuvToBgraConverter());
                        imageStopwatch.Stop();

                        progress?.Report(new VideoThumbnailResult(
                            index, Path.GetFullPath(outputPaths[index])));

                        Debug.WriteLine(
                            $"[Thumbnail {index}] COMPLETE thread={workerThreadId} " +
                            $"decode={decodeStopwatch.Elapsed}; image={imageStopwatch.Elapsed}; " +
                            $"total={totalStopwatch.Elapsed}.");
                    }
                    catch (Exception exception) when (
                        exception is not OperationCanceledException)
                    {
                        Debug.WriteLine(
                            $"[Thumbnail {index}] FAILED during image output " +
                            $"after {totalStopwatch.Elapsed}: {exception}");
                    }
                });

            batchStopwatch.Stop();
            Debug.WriteLine(
                $"[Thumbnails] Batch completed; thumbnails={positions.Count}; " +
                $"workers={workerCount}; elapsed={batchStopwatch.Elapsed}.");
        }, cancellationToken);
    }

    internal static int GetThumbnailWorkerCount(
        int thumbnailCount,
        int processorCount,
        int? requestedWorkerCount = null)
    {
        if (thumbnailCount <= 0)
            return 0;
        if (processorCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(processorCount));
        if (requestedWorkerCount is <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedWorkerCount));

        int maximumWorkers = Math.Min(processorCount, MaximumThumbnailParallelism);
        int selectedWorkers = Math.Min(maximumWorkers, requestedWorkerCount ?? maximumWorkers);
        return Math.Min(thumbnailCount, selectedWorkers);
    }

    private static int? GetRequestedWorkerCount()
    {
        string? value = Environment.GetEnvironmentVariable(WorkerCountEnvironmentVariable);
        return int.TryParse(value, out int workerCount) && workerCount > 0
            ? workerCount
            : null;
    }

    private static bool IsCabacProfilingEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable(CabacProfilingEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    private static void SaveThumbnail(
        H264Mp4DecodedVideoFrame frame,
        string outputPath,
        H264AvcParameterSetCollectionReader parameterSetReader,
        H264YuvToBgraConverter converter)
    {
        H264AvcParameterSetCollection parameterSets =
            parameterSetReader.Read(frame.SourceSample.Configuration);
        H264SequenceParameterSet sps =
            parameterSets.SequenceParameterSets.Values.FirstOrDefault()
            ?? throw new InvalidDataException(
                "The AVC configuration does not contain a usable sequence parameter set.");

        H264BgraFrame bgra = converter.Convert(frame.Surface, sps);

        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);

        using SKBitmap source = new(
            bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Marshal.Copy(bgra.Pixels, 0, source.GetPixels(), bgra.Pixels.Length);

        const int thumbnailWidth = 240;
        int thumbnailHeight = Math.Max(
            1,
            (int)Math.Round((double)bgra.Height * thumbnailWidth / bgra.Width));

        using SKBitmap thumbnail = new(
            thumbnailWidth, thumbnailHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (SKCanvas canvas = new(thumbnail))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(
                source, new SKRect(0, 0, thumbnailWidth, thumbnailHeight));
        }

        using SKImage image = SKImage.FromBitmap(thumbnail);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 90)
            ?? throw new InvalidOperationException(
                "SkiaSharp could not encode the decoded thumbnail.");

        using FileStream output = new(
            fullOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoded.SaveTo(output);
    }
}

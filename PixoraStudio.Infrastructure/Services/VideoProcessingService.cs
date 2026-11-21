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
/// Generates thumbnails with the Windows Media Foundation hardware decoder when
/// available, falling back to PixoraStudio's managed MP4 reader and H.264 decoder.
/// </summary>
public sealed class VideoProcessingService : IVideoProcessingService
{
    private readonly IHardwareVideoThumbnailDecoder? _hardwareDecoder;

    public VideoProcessingService(IHardwareVideoThumbnailDecoder? hardwareDecoder = null)
    {
        _hardwareDecoder = hardwareDecoder;
    }

    private const int MaximumThumbnailParallelism = 4;
    private const string WorkerCountEnvironmentVariable = "PIXORA_H264_THUMBNAIL_WORKERS";
    private const string CabacProfilingEnvironmentVariable = "PIXORA_H264_CABAC_PROFILE";

    /// <summary>Width of generated filmstrip thumbnails in pixels.</summary>
    private const int ThumbnailWidth = 240;

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
    /// Parses MP4 metadata and builds the track's seek index once, then groups
    /// thumbnails by the keyframe they decode from. Each group is decoded in a
    /// single pass (or, in <see cref="VideoThumbnailMode.NearestKeyframe"/>
    /// mode, as a single picture), and groups run in parallel on independent
    /// decoder state.
    /// </summary>
    public Task GenerateThumbnailsAsync(
        string filePath,
        IReadOnlyList<TimeSpan> positions,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress = null,
        CancellationToken cancellationToken = default,
        VideoThumbnailMode mode = VideoThumbnailMode.Exact)
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

        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_hardwareDecoder is not null)
            {
                try
                {
                    bool hardwareHandled = await _hardwareDecoder.TryGenerateThumbnailsAsync(
                        filePath, positions, outputPaths, progress, cancellationToken)
                        .ConfigureAwait(false);
                    if (hardwareHandled)
                        return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Debug.WriteLine(
                        $"[Media Foundation] Hardware thumbnail decoding failed; " +
                        $"using the managed decoder instead: {exception}");
                }
            }

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

            // Thumbnails that seek to the same keyframe share one decode pass.
            // Decoding each of them separately repeats every picture between
            // the keyframe and the earlier targets.
            IReadOnlyList<ThumbnailGroup> groups = GroupBySeekSample(
                seekPlans.Select(plan => plan.FirstSampleToDecode).ToArray());

            int? requestedWorkerCount = GetRequestedWorkerCount();
            int workerCount = GetThumbnailWorkerCount(
                groups.Count, Environment.ProcessorCount, requestedWorkerCount);
            bool enableCabacProfiling = IsCabacProfilingEnabled();
            Debug.WriteLine(
                $"[Thumbnails] mode={mode}; thumbnails={seekPlans.Length}; " +
                $"decodeGroups={groups.Count}; thumbnailWorkers={workerCount}; " +
                $"sliceWorkers={H264Mp4AccessUnitDecoder.ConfiguredMaxSliceParallelism}; " +
                $"processors={Environment.ProcessorCount}; " +
                $"cabacProfiling={enableCabacProfiling}.");

            Stopwatch batchStopwatch = Stopwatch.StartNew();
            Parallel.For(
                0,
                groups.Count,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = workerCount
                },
                groupIndex =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ThumbnailGroup group = groups[groupIndex];
                    Stopwatch groupStopwatch = Stopwatch.StartNew();

                    using FileStream stream = new(
                        filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

                    H264CabacPerformanceProfiler? cabacProfiler =
                        enableCabacProfiling ? new H264CabacPerformanceProfiler() : null;
                    H264Mp4TrackDecoder trackDecoder = new(cabacProfiler);

                    bool handled =
                        mode == VideoThumbnailMode.NearestKeyframe &&
                        TryWriteKeyframeThumbnails(
                            trackDecoder, stream, mediaInfo, videoTrack.TrackId,
                            group, outputPaths, progress, cancellationToken);

                    if (!handled)
                    {
                        WriteExactThumbnails(
                            trackDecoder, stream, mediaInfo, videoTrack.TrackId,
                            group, seekPlans, outputPaths, progress, cancellationToken);
                    }

                    if (cabacProfiler is not null)
                    {
                        H264CabacPerformanceSnapshot snapshot = cabacProfiler.GetSnapshot();
                        Debug.WriteLine(
                            $"[H264 CABAC profile] group={groupIndex}; " +
                            $"decisionBins={snapshot.DecisionBinCount}; " +
                            $"decisionElapsed={snapshot.DecisionElapsed}; " +
                            $"bypassBins={snapshot.BypassBinCount}; " +
                            $"bypassElapsed={snapshot.BypassElapsed}; " +
                            $"renormalizations={snapshot.RenormalizationCount}; " +
                            $"renormalizationElapsed={snapshot.RenormalizationElapsed} " +
                            "(included in decisionElapsed).");
                    }

                    Debug.WriteLine(
                        $"[Thumbnail group {groupIndex}] firstSample={group.FirstSample}; " +
                        $"thumbnails={group.Indices.Length}; keyframeOnly={handled}; " +
                        $"elapsed={groupStopwatch.Elapsed}.");
                });

            batchStopwatch.Stop();
            Debug.WriteLine(
                $"[Thumbnails] Batch completed; thumbnails={positions.Count}; " +
                $"groups={groups.Count}; workers={workerCount}; elapsed={batchStopwatch.Elapsed}.");
        }, cancellationToken);
    }

    /// <summary>
    /// A run of consecutive thumbnails that decode from the same keyframe.
    /// <see cref="Indices"/> are indices into the caller's position list.
    /// </summary>
    internal readonly record struct ThumbnailGroup(uint FirstSample, int[] Indices);

    /// <summary>
    /// Splits per-thumbnail seek samples into runs of equal consecutive values.
    /// Positions are ascending, so thumbnails that share a keyframe are adjacent.
    /// </summary>
    internal static IReadOnlyList<ThumbnailGroup> GroupBySeekSample(
        IReadOnlyList<uint> firstSamples)
    {
        ArgumentNullException.ThrowIfNull(firstSamples);

        List<ThumbnailGroup> groups = [];
        int start = 0;
        for (int index = 1; index <= firstSamples.Count; index++)
        {
            if (index < firstSamples.Count && firstSamples[index] == firstSamples[start])
                continue;

            groups.Add(new ThumbnailGroup(
                firstSamples[start],
                Enumerable.Range(start, index - start).ToArray()));
            start = index;
        }

        return groups;
    }

    /// <summary>
    /// Decodes just the group's keyframe and writes it for every thumbnail in
    /// the group. Returns false, having written nothing, if the keyframe could
    /// not be decoded on its own (for example a recovery-point stream whose sync
    /// sample is not an IDR picture), so the caller can use an exact decode.
    /// </summary>
    private static bool TryWriteKeyframeThumbnails(
        H264Mp4TrackDecoder trackDecoder,
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        ThumbnailGroup group,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress,
        CancellationToken cancellationToken)
    {
        byte[] png;
        try
        {
            H264Mp4DecodedVideoFrame? frame = trackDecoder.DecodeSyncSample(
                stream, mediaInfo, trackId, group.FirstSample,
                cancellationToken: cancellationToken);
            // Only an IDR picture is guaranteed not to reference earlier
            // pictures. A non-IDR sync sample (open GOP, gradual refresh) may
            // decode to a wrong image without throwing, so use the exact path.
            if (frame is null ||
                !frame.SourceSample.NalUnits.Any(nal => nal.NalUnitType == 5))
                return false;

            png = EncodeThumbnail(
                frame, new H264AvcParameterSetCollectionReader(), new H264YuvToBgraConverter());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Debug.WriteLine(
                $"[Thumbnail group] keyframe-only decode of sample {group.FirstSample} " +
                $"failed; falling back to exact decode: {exception.Message}");
            return false;
        }

        // One decode and one encode serve every thumbnail that maps to this keyframe.
        foreach (int index in group.Indices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ReportThumbnail(index, outputPaths[index], png, progress);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Debug.WriteLine($"[Thumbnail {index}] FAILED during image output: {exception}");
            }
        }

        return true;
    }

    /// <summary>
    /// Decodes forward from the group's keyframe once, writing each thumbnail as
    /// the frame at its requested position emerges in presentation order.
    /// </summary>
    private static void WriteExactThumbnails(
        H264Mp4TrackDecoder trackDecoder,
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        ThumbnailGroup group,
        IReadOnlyList<H264Mp4VideoSeekPlan> seekPlans,
        IReadOnlyList<string> outputPaths,
        IProgress<VideoThumbnailResult>? progress,
        CancellationToken cancellationToken)
    {
        long[] targets = group.Indices
            .Select(index => seekPlans[index].TargetPresentationTimeTicks)
            .ToArray();

        H264AvcParameterSetCollectionReader parameterSetReader = new();
        H264YuvToBgraConverter converter = new();
        int written = 0;

        foreach (H264Mp4TargetFrame hit in trackDecoder.DecodeTargetsFromSample(
            stream, mediaInfo, trackId, group.FirstSample, targets,
            cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = group.Indices[hit.TargetIndex];

            try
            {
                byte[] png = EncodeThumbnail(hit.Frame, parameterSetReader, converter);
                ReportThumbnail(index, outputPaths[index], png, progress);
                written++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Debug.WriteLine($"[Thumbnail {index}] FAILED during image output: {exception}");
            }
        }

        if (written < group.Indices.Length)
        {
            Debug.WriteLine(
                $"[Thumbnail group {group.FirstSample}] produced {written} of " +
                $"{group.Indices.Length} thumbnails.");
        }
    }

    private static void ReportThumbnail(
        int index,
        string outputPath,
        byte[] png,
        IProgress<VideoThumbnailResult>? progress)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        File.WriteAllBytes(fullOutputPath, png);
        progress?.Report(new VideoThumbnailResult(index, fullOutputPath));
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

    private static byte[] EncodeThumbnail(
        H264Mp4DecodedVideoFrame frame,
        H264AvcParameterSetCollectionReader parameterSetReader,
        H264YuvToBgraConverter converter)
    {
        H264AvcParameterSetCollection parameterSets =
            parameterSetReader.Read(frame.SourceSample.Configuration);
        H264SequenceParameterSet sps =
            parameterSets.SequenceParameterSets.Values.FirstOrDefault()
            ?? throw new InvalidDataException(
                "The AVC configuration does not contain a usable sequence parameter set.");

        // Convert straight to thumbnail size instead of building a full-size
        // BGRA frame and downscaling it afterwards.
        H264BgraFrame bgra = converter.ConvertScaled(frame.Surface, sps, ThumbnailWidth);

        using SKBitmap bitmap = new(
            bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        Marshal.Copy(bgra.Pixels, 0, bitmap.GetPixels(), bgra.Pixels.Length);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 90)
            ?? throw new InvalidOperationException(
                "SkiaSharp could not encode the decoded thumbnail.");

        return encoded.ToArray();
    }
}

using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;
using System.Diagnostics;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Decodes an MP4 AVC track in decode order and yields pictures in H.264
/// presentation order. The caller supplies the parsed MP4 movie/track boxes;
/// all emitted frames retain their originating sample timestamps.
/// </summary>
public sealed class H264Mp4TrackDecoder
{
    private readonly Mp4TrackSamplePipelineReader _samplePipelineReader = new();
    private readonly H264Mp4TrackSampleReader _sampleReader = new();
    private readonly H264CabacPerformanceProfiler? _cabacPerformanceProfiler;

    public H264Mp4TrackDecoder(
        H264CabacPerformanceProfiler? cabacPerformanceProfiler = null)
    {
        _cabacPerformanceProfiler = cabacPerformanceProfiler;
    }

    public H264CabacPerformanceProfiler? CabacPerformanceProfiler =>
        _cabacPerformanceProfiler;

    public IEnumerable<H264Mp4DecodedVideoFrame> Decode(
        Stream stream,
        Mp4Box root,
        Mp4Box trackBox,
        Mp4Track track,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(trackBox);
        ArgumentNullException.ThrowIfNull(track);

        IEnumerable<Mp4EncodedSample> samples = _samplePipelineReader.Read(
            stream, root, trackBox, track, cancellationToken);
        return DecodeSamples(track, samples, maxNumReorderFramesFallback, cancellationToken);
    }

    public IEnumerable<H264Mp4DecodedVideoFrame> Decode(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ValidateStream(stream);

        Mp4MediaTrack mediaTrack = GetVideoTrack(mediaInfo, trackId);
        IEnumerable<Mp4EncodedSample> samples = mediaTrack.ReadSamples(
            stream, mediaInfo.MediaDataRegions, cancellationToken);
        return DecodeSamples(
            mediaTrack.Track, samples, maxNumReorderFramesFallback, cancellationToken);
    }

    /// <summary>
    /// Plans and decodes from the random-access sample preceding the requested
    /// presentation time. Use DecodeFromPlan when multiple seeks target the
    /// same track so the caller can reuse a precomputed seek index.
    /// </summary>
    public IEnumerable<H264Mp4DecodedVideoFrame> DecodeFromTime(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        double targetPresentationTimeSeconds,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        Mp4MediaTrack mediaTrack = GetVideoTrack(mediaInfo, trackId);
        H264Mp4VideoSeekPlan plan = new H264Mp4VideoSeekPlanner().Plan(
            mediaTrack, targetPresentationTimeSeconds);

        return DecodeFromPlan(
            stream, mediaInfo, trackId, plan,
            maxNumReorderFramesFallback, cancellationToken);
    }

    /// <summary>
    /// Decodes using a previously calculated seek plan. This avoids rebuilding
    /// the track's seek index when generating multiple thumbnails for one video.
    /// </summary>
    public IEnumerable<H264Mp4DecodedVideoFrame> DecodeFromPlan(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        H264Mp4VideoSeekPlan plan,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(plan);
        ValidateStream(stream);

        Mp4MediaTrack mediaTrack = GetVideoTrack(mediaInfo, trackId);
        if (plan.Timescale != mediaTrack.Timescale)
            throw new ArgumentException(
                "The seek plan timescale does not match the selected video track.",
                nameof(plan));
        if (plan.FirstSampleToDecode == 0 ||
            plan.FirstSampleToDecode > mediaTrack.SampleCount)
            throw new ArgumentOutOfRangeException(
                nameof(plan), "The seek plan references a sample outside the video track.");

        uint remainingSamples = checked(
            mediaTrack.SampleCount - plan.FirstSampleToDecode + 1);
        IEnumerable<Mp4EncodedSample> samples = mediaTrack.ReadSamples(
            stream,
            plan.FirstSampleToDecode,
            remainingSamples,
            mediaInfo.MediaDataRegions,
            cancellationToken);

        return DecodeFromSeekPoint(
            mediaTrack.Track, samples, plan,
            maxNumReorderFramesFallback, cancellationToken);
    }

    /// <summary>
    /// Decodes forward once from a random-access sample and yields, for each
    /// ascending presentation-time target, the first frame whose presentation
    /// interval ends after that target. This is the same frame
    /// <see cref="DecodeFromPlan"/> returns for a single target, but every
    /// target that shares the random-access point is served by one decode pass
    /// instead of each target re-decoding the same pictures. Decoding stops as
    /// soon as the last target has been satisfied.
    /// </summary>
    public IEnumerable<H264Mp4TargetFrame> DecodeTargetsFromSample(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        uint firstSampleToDecode,
        IReadOnlyList<long> targetPresentationTimeTicks,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(targetPresentationTimeTicks);
        ValidateStream(stream);

        for (int index = 1; index < targetPresentationTimeTicks.Count; index++)
        {
            if (targetPresentationTimeTicks[index] < targetPresentationTimeTicks[index - 1])
                throw new ArgumentException(
                    "Target presentation times must be in ascending order.",
                    nameof(targetPresentationTimeTicks));
        }

        Mp4MediaTrack mediaTrack = GetVideoTrack(mediaInfo, trackId);
        if (firstSampleToDecode == 0 || firstSampleToDecode > mediaTrack.SampleCount)
            throw new ArgumentOutOfRangeException(
                nameof(firstSampleToDecode),
                "The first sample is outside the video track.");

        if (targetPresentationTimeTicks.Count == 0)
            return [];

        uint remainingSamples = checked(
            mediaTrack.SampleCount - firstSampleToDecode + 1);
        IEnumerable<Mp4EncodedSample> samples = mediaTrack.ReadSamples(
            stream,
            firstSampleToDecode,
            remainingSamples,
            mediaInfo.MediaDataRegions,
            cancellationToken);

        return DecodeTargets(
            mediaTrack.Track,
            samples,
            targetPresentationTimeTicks,
            maxNumReorderFramesFallback,
            cancellationToken);
    }

    /// <summary>
    /// Decodes only the given sync sample and returns its picture, without
    /// decoding any of the P/B pictures that follow it. This is keyframe-only
    /// seeking: it costs one picture instead of every picture between the
    /// keyframe and an exact target. The sample must be
    /// self-contained (an IDR or an I picture that does not reference earlier
    /// pictures); callers should fall back to an exact decode if this throws.
    /// </summary>
    public H264Mp4DecodedVideoFrame? DecodeSyncSample(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        uint sampleNumber,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ValidateStream(stream);

        Mp4MediaTrack mediaTrack = GetVideoTrack(mediaInfo, trackId);
        if (sampleNumber == 0 || sampleNumber > mediaTrack.SampleCount)
            throw new ArgumentOutOfRangeException(
                nameof(sampleNumber),
                "The sample is outside the video track.");

        IEnumerable<Mp4EncodedSample> samples = mediaTrack.ReadSamples(
            stream,
            sampleNumber,
            1,
            mediaInfo.MediaDataRegions,
            cancellationToken);

        return DecodeSamples(
                mediaTrack.Track, samples, maxNumReorderFramesFallback, cancellationToken)
            .FirstOrDefault();
    }

    private IEnumerable<H264Mp4TargetFrame> DecodeTargets(
        Mp4Track track,
        IEnumerable<Mp4EncodedSample> samples,
        IReadOnlyList<long> targetPresentationTimeTicks,
        int? maxNumReorderFramesFallback,
        CancellationToken cancellationToken)
    {
        int nextTarget = 0;

        foreach (H264Mp4DecodedVideoFrame frame in DecodeSamples(
            track, samples, maxNumReorderFramesFallback, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            long presentationEnd = GetPresentationEnd(frame);
            while (nextTarget < targetPresentationTimeTicks.Count &&
                   presentationEnd > targetPresentationTimeTicks[nextTarget])
            {
                yield return new H264Mp4TargetFrame(nextTarget, frame);
                nextTarget++;
            }

            if (nextTarget == targetPresentationTimeTicks.Count)
                yield break;
        }
    }

    private static long GetPresentationEnd(H264Mp4DecodedVideoFrame frame)
    {
        try
        {
            return checked(frame.PresentationTimeTicks + frame.DurationTicks);
        }
        catch (OverflowException exception)
        {
            throw new FormatException(
                "A decoded MP4 frame has an overflowing presentation interval.", exception);
        }
    }

    private static Mp4MediaTrack GetVideoTrack(Mp4MediaInfo mediaInfo, uint trackId)
    {
        Mp4MediaTrack mediaTrack = mediaInfo.GetTrack(trackId) ??
            throw new ArgumentException($"MP4 track {trackId} does not exist.", nameof(trackId));
        if (mediaTrack.HandlerType != Mp4HandlerType.Video)
            throw new NotSupportedException($"MP4 track {trackId} is not a video track.");
        return mediaTrack;
    }

    private static void ValidateStream(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException(
                "The MP4 stream must support reading and seeking.", nameof(stream));
    }

    private IEnumerable<H264Mp4DecodedVideoFrame> DecodeFromSeekPoint(
        Mp4Track track,
        IEnumerable<Mp4EncodedSample> samples,
        H264Mp4VideoSeekPlan plan,
        int? maxNumReorderFramesFallback,
        CancellationToken cancellationToken)
    {
        foreach (H264Mp4DecodedVideoFrame frame in DecodeSamples(
            track, samples, maxNumReorderFramesFallback, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            long presentationEnd;
            try
            {
                presentationEnd = checked(frame.PresentationTimeTicks + frame.DurationTicks);
            }
            catch (OverflowException exception)
            {
                throw new FormatException(
                    "A decoded MP4 frame has an overflowing presentation interval.", exception);
            }

            if (presentationEnd > plan.TargetPresentationTimeTicks)
                yield return frame;
        }
    }

    public IEnumerable<H264Mp4DecodedVideoFrame> DecodeSamples(
        Mp4Track track,
        IEnumerable<Mp4EncodedSample> samples,
        int? maxNumReorderFramesFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(samples);
        if (track.HandlerType != Mp4HandlerType.Video)
            throw new NotSupportedException("The selected MP4 track is not a video track.");

        return DecodeIterator(track, samples, maxNumReorderFramesFallback, cancellationToken);
    }

    private IEnumerable<H264Mp4DecodedVideoFrame> DecodeIterator(
        Mp4Track track,
        IEnumerable<Mp4EncodedSample> samples,
        int? maxNumReorderFramesFallback,
        CancellationToken cancellationToken)
    {
        H264Mp4AccessUnitDecoder? decoder = null;
        int decodedSampleCount = 0;
        TimeSpan sampleReadElapsed = TimeSpan.Zero;
        TimeSpan accessUnitDecodeElapsed = TimeSpan.Zero;

        try
        {
            foreach (Mp4EncodedSample encodedSample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Stopwatch sampleReadStopwatch = Stopwatch.StartNew();
                H264Mp4VideoSample sample = _sampleReader.Read(track, encodedSample);
                sampleReadStopwatch.Stop();
                sampleReadElapsed += sampleReadStopwatch.Elapsed;

                Stopwatch decodeStopwatch = Stopwatch.StartNew();
                decoder ??= H264Mp4AccessUnitDecoder.CreateFromSample(
                    sample, maxNumReorderFramesFallback, _cabacPerformanceProfiler);
                H264Mp4AccessUnitDecodeResult? result = decoder.Decode(sample);
                decodeStopwatch.Stop();
                accessUnitDecodeElapsed += decodeStopwatch.Elapsed;
                decodedSampleCount++;

                if (result is null)
                    continue;

                foreach (H264Mp4DecodedVideoFrame frame in result.ReadyFrames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return frame;
                }
            }

            if (decoder is null)
                yield break;

            foreach (H264Mp4DecodedVideoFrame frame in decoder.Flush())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frame;
            }
        }
        finally
        {
            Debug.WriteLine(
                $"[H264 seek] track={track.TrackId}; samples={decodedSampleCount}; " +
                $"sampleRead={sampleReadElapsed}; accessUnitDecode={accessUnitDecodeElapsed}.");
        }
    }
}

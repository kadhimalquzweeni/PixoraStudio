using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.App.Services;

/// <summary>
/// Optional managed AVC preview path. LibVLC remains the playback clock and
/// fallback renderer; this service supplies managed BGRA frames for supported
/// non-fragmented AVC MP4 tracks.
/// </summary>
public sealed class ManagedH264VideoPlaybackService : IAsyncDisposable
{
    private const int DecodeBufferCapacity = 3;
    private const int KeyframePreviewCacheCapacity = 32;

    private readonly H264Mp4FramePresentationScheduler _presentationScheduler = new();
    private readonly H264YuvToBgraConverter _converter = new();
    private readonly H264Mp4TrackDecoder _decoder = new();
    private readonly H264AvcParameterSetCollectionReader _parameterSetReader = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private Mp4MediaInfo? _mediaInfo;
    private uint _trackId;
    private string? _filePath;
    private Func<double>? _playbackClock;
    private Action<BitmapSource>? _frameReady;
    private Action<Exception>? _failed;
    private CancellationTokenSource? _decodeCancellation;
    private Task? _decodeTask;

    // Keyframe pictures shown while scrubbing, keyed by MP4 sample number.
    // Dragging back and forth over the same keyframes then costs nothing.
    // Only touched while holding _operationLock.
    private readonly Dictionary<uint, BitmapSource> _keyframePreviews = new();
    private readonly Queue<uint> _keyframePreviewOrder = new();
    private readonly H264Mp4VideoSeekPlanner _seekPlanner = new();


    public bool IsActive => _mediaInfo is not null && _decodeCancellation is not null;

    public async Task<bool> TryStartAsync(
        string filePath,
        Func<double> playbackClock,
        Action<BitmapSource> frameReady,
        Action<Exception> failed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(playbackClock);
        ArgumentNullException.ThrowIfNull(frameReady);
        ArgumentNullException.ThrowIfNull(failed);

        await StopAsync().ConfigureAwait(false);

        (Mp4MediaInfo Info, uint TrackId)? prepared;
        try
        {
            prepared = await Task.Run<(Mp4MediaInfo Info, uint TrackId)?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using FileStream stream = new(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Mp4MediaInfo info = new Mp4FileReader().Read(stream);
                Mp4MediaTrack? track = info.GetTracks(Mp4HandlerType.Video)
                    .FirstOrDefault(candidate => candidate.Track.SampleDescriptions.Any(description =>
                        description.Video is
                        {
                            Codec: PixoraStudio.MediaEngine.Models.Video.Mp4VideoCodec.Avc,
                            CodecConfiguration: not null
                        } video &&
                        (video.Format is "avc1" or "avc3")));
                return track is null ? null : (info, track.TrackId);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or FormatException or NotSupportedException or Mp4ParsingException or
            ArgumentException or InvalidOperationException)
        {
            return false;
        }

        if (prepared is null)
            return false;

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _mediaInfo = prepared.Value.Info;
            _trackId = prepared.Value.TrackId;
            _filePath = filePath;
            _playbackClock = playbackClock;
            _frameReady = frameReady;
            _failed = failed;
            ClearKeyframePreviews();

            await StartDecodeAsync(0, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task SeekAsync(
        double positionSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(positionSeconds) || positionSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(positionSeconds));
        if (_mediaInfo is null || _filePath is null)
            return;

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopDecodeTaskAsync().ConfigureAwait(false);
            await StartDecodeAsync(positionSeconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Publishes the keyframe at or before <paramref name="positionSeconds"/>
    /// without waiting for the playback clock. Decoding one picture costs a
    /// fraction of decoding forward to the exact frame, so this keeps a drag
    /// responsive on videos with long keyframe intervals. Call
    /// <see cref="SeekAsync"/> afterwards to land on the exact frame.
    /// </summary>
    public async Task ShowKeyframePreviewAsync(
        double positionSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(positionSeconds) || positionSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(positionSeconds));

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Mp4MediaInfo? info = _mediaInfo;
            string? path = _filePath;
            Action<BitmapSource>? publish = _frameReady;
            if (info is null || path is null || publish is null)
                return;

            // Playback would otherwise overwrite the preview with later frames.
            await StopDecodeTaskAsync().ConfigureAwait(false);

            uint trackId = _trackId;
            BitmapSource? preview = await Task.Run(() =>
            {
                Mp4MediaTrack track = info.GetTrack(trackId) ??
                    throw new InvalidOperationException("The active MP4 video track is unavailable.");
                uint sampleNumber = _seekPlanner
                    .Plan(track, positionSeconds)
                    .FirstSampleToDecode;

                if (_keyframePreviews.TryGetValue(sampleNumber, out BitmapSource? cached))
                    return cached;

                using FileStream stream = new(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read);
                H264Mp4DecodedVideoFrame? frame = _decoder.DecodeSyncSample(
                    stream, info, trackId, sampleNumber,
                    cancellationToken: cancellationToken);
                if (frame is null)
                    return null;

                BitmapSource created = ConvertToBitmap(
                    frame, new(ReferenceEqualityComparer.Instance));
                RememberKeyframePreview(sampleNumber, created);
                return created;
            }, cancellationToken).ConfigureAwait(false);

            if (preview is not null)
                publish(preview);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void RememberKeyframePreview(uint sampleNumber, BitmapSource bitmap)
    {
        if (_keyframePreviews.TryAdd(sampleNumber, bitmap))
            _keyframePreviewOrder.Enqueue(sampleNumber);

        while (_keyframePreviewOrder.Count > KeyframePreviewCacheCapacity)
            _keyframePreviews.Remove(_keyframePreviewOrder.Dequeue());
    }

    private void ClearKeyframePreviews()
    {
        _keyframePreviews.Clear();
        _keyframePreviewOrder.Clear();
    }

    private BitmapSource ConvertToBitmap(
        H264Mp4DecodedVideoFrame frame,
        Dictionary<H264AvcDecoderConfiguration, H264AvcParameterSetCollection> parameterSetCache)
    {
        H264AvcDecoderConfiguration configuration = frame.SourceSample.Configuration;
        if (!parameterSetCache.TryGetValue(
                configuration, out H264AvcParameterSetCollection? parameterSets) ||
            parameterSets is null)
        {
            parameterSets = _parameterSetReader.Read(configuration);
            parameterSetCache.Add(configuration, parameterSets);
        }

        H264SequenceParameterSet sps = parameterSets.SequenceParameterSets.Values.First();
        H264BgraFrame bgra = _converter.Convert(frame.Surface, sps);
        BitmapSource bitmap = BitmapSource.Create(
            bgra.Width, bgra.Height, 96, 96,
            PixelFormats.Bgra32, null, bgra.Pixels, bgra.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public async Task StopAsync()
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopDecodeTaskAsync().ConfigureAwait(false);
            _mediaInfo = null;
            _filePath = null;
            _playbackClock = null;
            _frameReady = null;
            _failed = null;
            _trackId = 0;
            ClearKeyframePreviews();
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private Task StartDecodeAsync(
        double startSeconds,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _decodeCancellation = linked;
        _decodeTask = Task.Run(
            () => RunDecodePipelineAsync(startSeconds, linked.Token),
            CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Decoding and presentation run independently. A small bounded buffer
    /// lets the decoder stay ahead of the playback clock without allowing a
    /// long video to accumulate decoded frames in memory.
    /// </summary>
    private async Task RunDecodePipelineAsync(
        double startSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            string path = _filePath ??
                throw new InvalidOperationException("No AVC file is loaded.");
            Mp4MediaInfo info = _mediaInfo ??
                throw new InvalidOperationException("No MP4 metadata is loaded.");
            Func<double> clock = _playbackClock ??
                throw new InvalidOperationException("No playback clock is configured.");
            Action<BitmapSource> publish = _frameReady ??
                throw new InvalidOperationException("No frame receiver is configured.");

            H264Mp4DecodedFrameBuffer buffer = new(DecodeBufferCapacity);
            Task decodeTask = DecodeFramesAsync(
                path, info, _trackId, startSeconds, buffer, cancellationToken);
            Task presentationTask = PresentFramesAsync(
                buffer, clock, publish, cancellationToken);

            await Task.WhenAll(decodeTask, presentationTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A seek, close or replacement file superseded this decode sequence.
        }
        catch (Exception exception)
        {
            _failed?.Invoke(exception);
        }
    }

    private async Task DecodeFramesAsync(
        string path,
        Mp4MediaInfo info,
        uint trackId,
        double startSeconds,
        H264Mp4DecodedFrameBuffer buffer,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            IEnumerable<H264Mp4DecodedVideoFrame> frames =
                startSeconds <= 0
                    ? _decoder.Decode(stream, info, trackId, cancellationToken: cancellationToken)
                    : _decoder.DecodeFromTime(
                        stream, info, trackId, startSeconds,
                        cancellationToken: cancellationToken);

            foreach (H264Mp4DecodedVideoFrame frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await buffer.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            buffer.Complete(failure);
        }
    }

    private async Task PresentFramesAsync(
        H264Mp4DecodedFrameBuffer buffer,
        Func<double> playbackClock,
        Action<BitmapSource> publish,
        CancellationToken cancellationToken)
    {
        Dictionary<H264AvcDecoderConfiguration, H264AvcParameterSetCollection>
            parameterSetCache = new(ReferenceEqualityComparer.Instance);
        try
        {
            await foreach (H264Mp4DecodedVideoFrame frame in
                buffer.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                H264FramePresentationDecision decision;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    decision = _presentationScheduler.Evaluate(
                        frame.SourceSample.PresentationTimeSeconds,
                        Math.Max(0, playbackClock()),
                        H264Mp4FramePresentationScheduler.GetMaximumLatenessSeconds(
                            frame.SourceSample.DurationSeconds));
                    if (decision.Action != H264FramePresentationAction.Wait)
                        break;

                    await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
                }

                if (decision.Action == H264FramePresentationAction.Drop)
                    continue;

                BitmapSource bitmap = ConvertToBitmap(frame, parameterSetCache);
                publish(bitmap);
            }

        }
        finally
        {
            // If presentation fails, unblock a producer waiting on a full buffer.
            buffer.Complete();
        }
    }

    private async Task StopDecodeTaskAsync()
    {
        CancellationTokenSource? cancellation = _decodeCancellation;
        Task? task = _decodeTask;
        _decodeCancellation = null;
        _decodeTask = null;

        if (cancellation is null)
            return;

        cancellation.Cancel();
        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when replacing the active decode sequence.
            }
        }

        cancellation.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

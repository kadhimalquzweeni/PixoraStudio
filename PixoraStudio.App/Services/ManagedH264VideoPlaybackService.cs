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

    private readonly H264Mp4FramePresentationScheduler _presentationScheduler = new();
    private readonly H264YuvToBgraConverter _converter = new();
    private readonly H264Mp4TrackDecoder _decoder = new();
    private readonly H264Mp4FrameNavigationPlanner _frameNavigationPlanner = new();
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

    public H264Mp4FrameNavigationTarget? LastPresentedFrame { get; private set; }

    public bool IsActive => _mediaInfo is not null && _decodeCancellation is not null;

    /// <summary>
    /// Finds the adjacent picture using the loaded track's exact presentation
    /// timestamps. Returns null when no managed AVC track is active or the
    /// requested direction is already at the beginning/end of the track.
    /// </summary>
    public H264Mp4FrameNavigationTarget? FindAdjacentFrame(
        double currentPresentationTimeSeconds,
        H264Mp4FrameNavigationDirection direction)
    {
        Mp4MediaInfo? info = _mediaInfo;
        if (info is null)
            return null;

        Mp4MediaTrack track = info.GetTrack(_trackId) ??
            throw new InvalidOperationException(
                $"The loaded MP4 video track {_trackId} is no longer available.");

        return _frameNavigationPlanner.FindAdjacentFrame(
            track, currentPresentationTimeSeconds, direction);
    }

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
            LastPresentedFrame = null;

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
            LastPresentedFrame = null;
            await StartDecodeAsync(positionSeconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// Restarts decoding at a specific MP4 sample and guarantees that the
    /// requested presentation frame is published even while the playback
    /// clock is paused. Later frames remain queued against the normal clock.
    /// </summary>
    public async Task SeekToFrameAsync(
        H264Mp4FrameNavigationTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        Mp4MediaInfo? info = _mediaInfo;
        if (info is null || _filePath is null)
            return;

        Mp4MediaTrack track = info.GetTrack(_trackId) ??
            throw new InvalidOperationException("The active MP4 video track is unavailable.");
        if (target.Timescale != track.Timescale ||
            target.SampleNumber == 0 ||
            target.SampleNumber > track.SampleCount)
        {
            throw new ArgumentException(
                "The frame target does not belong to the active MP4 video track.",
                nameof(target));
        }

        Mp4SampleLocation sample = track.SampleLocations.Samples[
            checked((int)target.SampleNumber - 1)];
        if (sample.SampleNumber != target.SampleNumber ||
            sample.PresentationTimeTicks != target.PresentationTimeTicks)
        {
            throw new ArgumentException(
                "The frame target timestamp does not match its MP4 sample.",
                nameof(target));
        }

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopDecodeTaskAsync().ConfigureAwait(false);
            LastPresentedFrame = null;
            await StartDecodeAsync(
                target.PresentationTimeSeconds,
                target.SampleNumber,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
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
            LastPresentedFrame = null;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private Task StartDecodeAsync(
        double startSeconds,
        CancellationToken cancellationToken) =>
        StartDecodeAsync(startSeconds, null, cancellationToken);

    private Task StartDecodeAsync(
        double startSeconds,
        uint? firstFrameSampleNumber,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _decodeCancellation = linked;
        _decodeTask = Task.Run(
            () => RunDecodePipelineAsync(
                startSeconds, firstFrameSampleNumber, linked.Token),
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
        uint? firstFrameSampleNumber,
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
                path, info, _trackId, startSeconds, firstFrameSampleNumber,
                buffer, cancellationToken);
            Task presentationTask = PresentFramesAsync(
                buffer, clock, publish, firstFrameSampleNumber, cancellationToken);

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
        uint? firstFrameSampleNumber,
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
        uint? firstFrameSampleNumber,
        CancellationToken cancellationToken)
    {
        bool waitingForRequestedFrame = firstFrameSampleNumber.HasValue;
        try
        {
            await foreach (H264Mp4DecodedVideoFrame frame in
                buffer.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (waitingForRequestedFrame)
                {
                    if (frame.SourceSample.SampleNumber != firstFrameSampleNumber)
                        continue;

                    waitingForRequestedFrame = false;
                }

                H264FramePresentationDecision decision;
                if (firstFrameSampleNumber.HasValue &&
                    frame.SourceSample.SampleNumber == firstFrameSampleNumber.Value)
                {
                    decision = new H264FramePresentationDecision(
                        H264FramePresentationAction.Present, TimeSpan.Zero);
                }
                else
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        decision = _presentationScheduler.Evaluate(
                            frame.SourceSample.PresentationTimeSeconds,
                            Math.Max(0, playbackClock()));
                        if (decision.Action != H264FramePresentationAction.Wait)
                            break;

                        await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (decision.Action == H264FramePresentationAction.Drop)
                    continue;

                H264AvcParameterSetCollection parameterSets =
                    _parameterSetReader.Read(frame.SourceSample.Configuration);
                H264SequenceParameterSet sps =
                    parameterSets.SequenceParameterSets.Values.First();
                H264BgraFrame bgra = _converter.Convert(frame.Surface, sps);
                BitmapSource bitmap = BitmapSource.Create(
                    bgra.Width, bgra.Height, 96, 96,
                    PixelFormats.Bgra32, null, bgra.Pixels, bgra.Stride);
                bitmap.Freeze();
                LastPresentedFrame = new H264Mp4FrameNavigationTarget(
                    frame.SourceSample.SampleNumber,
                    frame.SourceSample.PresentationTimeTicks,
                    frame.SourceSample.Timescale);
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

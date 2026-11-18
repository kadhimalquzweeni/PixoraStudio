using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.App.Services;

/// <summary>
/// Optional managed AVC preview path. LibVLC remains the playback clock and
/// fallback renderer; this service supplies managed BGRA frames for supported
/// non-fragmented AVC MP4 tracks.
/// </summary>
public sealed class ManagedH264VideoPlaybackService : IAsyncDisposable
{
    private readonly H264Mp4FramePresentationScheduler _presentationScheduler = new();
    private readonly H264YuvToBgraConverter _converter = new();
    private readonly H264Mp4TrackDecoder _decoder = new();
    private readonly H264AvcParameterSetCollectionReader _parameterSetReader = new();

    private Mp4MediaInfo? _mediaInfo;
    private uint _trackId;
    private string? _filePath;
    private Func<double>? _playbackClock;
    private Action<BitmapSource>? _frameReady;
    private Action<Exception>? _failed;
    private CancellationTokenSource? _decodeCancellation;
    private Task? _decodeTask;

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
                        description.Video is { Codec: PixoraStudio.MediaEngine.Models.Video.Mp4VideoCodec.Avc,
                            CodecConfiguration: not null } video &&
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

        _mediaInfo = prepared.Value.Info;
        _trackId = prepared.Value.TrackId;
        _filePath = filePath;
        _playbackClock = playbackClock;
        _frameReady = frameReady;
        _failed = failed;

        await StartDecodeAsync(0, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SeekAsync(
        double positionSeconds,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(positionSeconds) || positionSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(positionSeconds));
        if (_mediaInfo is null || _filePath is null)
            return;

        await StopDecodeTaskAsync().ConfigureAwait(false);
        await StartDecodeAsync(positionSeconds, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        await StopDecodeTaskAsync().ConfigureAwait(false);
        _mediaInfo = null;
        _filePath = null;
        _playbackClock = null;
        _frameReady = null;
        _failed = null;
        _trackId = 0;
    }

    private Task StartDecodeAsync(double startSeconds, CancellationToken cancellationToken)
    {
        CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _decodeCancellation = linked;
        _decodeTask = Task.Run(
            () => DecodeFramesAsync(startSeconds, linked.Token),
            CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task DecodeFramesAsync(double startSeconds, CancellationToken cancellationToken)
    {
        try
        {
            string path = _filePath ?? throw new InvalidOperationException("No AVC file is loaded.");
            Mp4MediaInfo info = _mediaInfo ?? throw new InvalidOperationException("No MP4 metadata is loaded.");
            Func<double> clock = _playbackClock ?? throw new InvalidOperationException("No playback clock is configured.");
            Action<BitmapSource> publish = _frameReady ?? throw new InvalidOperationException("No frame receiver is configured.");

            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            IEnumerable<H264Mp4DecodedVideoFrame> frames = startSeconds <= 0
                ? _decoder.Decode(stream, info, _trackId, cancellationToken: cancellationToken)
                : _decoder.DecodeFromTime(stream, info, _trackId, startSeconds,
                    cancellationToken: cancellationToken);

            foreach (H264Mp4DecodedVideoFrame frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                H264FramePresentationDecision decision;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    decision = _presentationScheduler.Evaluate(
                        frame.SourceSample.PresentationTimeSeconds,
                        Math.Max(0, clock()));
                    if (decision.Action != H264FramePresentationAction.Wait)
                        break;

                    await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
                }

                if (decision.Action == H264FramePresentationAction.Drop)
                    continue;

                H264AvcParameterSetCollection parameterSets =
                    _parameterSetReader.Read(frame.SourceSample.Configuration);
                H264SequenceParameterSet sps = parameterSets.SequenceParameterSets.Values.First();
                H264BgraFrame bgra = _converter.Convert(frame.Surface, sps);
                BitmapSource bitmap = BitmapSource.Create(
                    bgra.Width, bgra.Height, 96, 96,
                    PixelFormats.Bgra32, null, bgra.Pixels, bgra.Stride);
                bitmap.Freeze();
                publish(bitmap);
            }
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

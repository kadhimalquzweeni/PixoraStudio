using NAudio.Wave;
using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.App.Services;

/// <summary>
/// Decodes an MP4 AAC audio track to a temporary PCM WAV file and plays it
/// through the Windows audio device. LibVLC remains responsible for video.
/// </summary>
public sealed class AacAudioPlaybackService : IDisposable
{
    private WaveFileReader? _reader;
    private WaveOutEvent? _output;
    private string? _temporaryWavePath;
    private string? _sourcePath;
    private TimeSpan _duration;
    private TimeSpan _timelineOffset;
    private CancellationTokenSource? _seekCancellation;
    private bool _disposed;
    private int _loadGeneration;
    private int _isSeeking;
    private float _volume = 1f;
    private bool _isMuted;
    private bool _resumeAfterSeek;

    public bool IsLoaded => _reader is not null && _output is not null;

    public bool IsPlaying =>
        _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Position =>
        _reader is null ? TimeSpan.Zero : _timelineOffset + _reader.CurrentTime;

    public TimeSpan Duration => _duration;

    public bool IsSeeking => Volatile.Read(ref _isSeeking) != 0;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_output is not null)
                _output.Volume = _isMuted ? 0f : _volume;
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (_output is not null)
                _output.Volume = _isMuted ? 0f : _volume;
        }
    }

    public async Task<bool> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        int generation = Interlocked.Increment(ref _loadGeneration);
        _seekCancellation?.Cancel();
        _seekCancellation?.Dispose();
        _seekCancellation = null;
        StopAndRelease();
        _sourcePath = filePath;
        _timelineOffset = TimeSpan.Zero;
        _duration = TimeSpan.Zero;

        if (!File.Exists(filePath) ||
            !string.Equals(Path.GetExtension(filePath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? wavePath = await Task.Run(
            () => DecodeToWaveFile(filePath, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (generation != Volatile.Read(ref _loadGeneration))
        {
            if (wavePath is not null && File.Exists(wavePath))
                File.Delete(wavePath);
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            if (wavePath is not null && File.Exists(wavePath))
                File.Delete(wavePath);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (wavePath is null)
            return false;

        try
        {
            _temporaryWavePath = wavePath;
            _reader = new WaveFileReader(wavePath);
            _output = new WaveOutEvent
            {
                DesiredLatency = 100
            };

            _output.Init(_reader);
            _output.Volume = _isMuted ? 0f : _volume;
            _duration = _reader.TotalTime;
            return true;
        }
        catch
        {
            StopAndRelease();
            throw;
        }
    }

    public async Task PlayAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsSeeking)
        {
            _resumeAfterSeek = true;
            return;
        }

        if (_output is null || _reader is null)
            return;

        if (_reader.Position >= _reader.Length)
        {
            if (_timelineOffset > TimeSpan.Zero)
            {
                await SeekAsync(TimeSpan.Zero, resumePlayback: true)
                    .ConfigureAwait(false);
                return;
            }

            _reader.Position = 0;
        }

        _output.Play();
    }

    public void Pause()
    {
        if (_disposed)
            return;

        if (IsSeeking)
        {
            _resumeAfterSeek = false;
            return;
        }

        _output?.Pause();
    }

    public void Unload()
    {
        Interlocked.Increment(ref _loadGeneration);
        _seekCancellation?.Cancel();
        _seekCancellation?.Dispose();
        _seekCancellation = null;
        Interlocked.Exchange(ref _isSeeking, 0);
        if (!_disposed)
            StopAndRelease();
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _output?.Stop();

        if (_reader is not null)
        {
            _reader.Position = _reader.Length;
            _timelineOffset = _duration - _reader.TotalTime;
        }
    }

    /// <summary>
    /// Rebuilds AAC decoder state from the nearest preceding access unit and
    /// replaces the active PCM segment at the requested presentation time.
    /// </summary>
    public async Task SeekAsync(
        TimeSpan position,
        bool resumePlayback,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_reader is null || _output is null)
            return;

        TimeSpan target = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > _duration
                ? _duration
                : position;

        if (target >= _duration)
        {
            _output.Stop();
            _reader.Position = _reader.Length;
            _timelineOffset = _duration - _reader.TotalTime;
            return;
        }

        if (string.IsNullOrWhiteSpace(_sourcePath))
        {
            _output.Stop();
            _reader.CurrentTime = target;
            _timelineOffset = TimeSpan.Zero;
            if (resumePlayback)
                _output.Play();
            return;
        }

        int generation = Interlocked.Increment(ref _loadGeneration);
        _seekCancellation?.Cancel();
        _seekCancellation?.Dispose();
        _seekCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        CancellationToken token = _seekCancellation.Token;
        float volume = Volume;
        _resumeAfterSeek = resumePlayback;

        Interlocked.Exchange(ref _isSeeking, 1);
        _output.Stop();

        try
        {
            string? wavePath = await Task.Run(
                () => DecodeSeekToWaveFile(
                    _sourcePath,
                    target.TotalSeconds,
                    token),
                token).ConfigureAwait(false);

            if (generation != Volatile.Read(ref _loadGeneration) ||
                token.IsCancellationRequested)
            {
                if (wavePath is not null && File.Exists(wavePath))
                    File.Delete(wavePath);
                return;
            }

            if (wavePath is null)
            {
                _reader.CurrentTime = target;
                _timelineOffset = TimeSpan.Zero;
                if (_resumeAfterSeek)
                    _output.Play();
                return;
            }

            StopAndRelease();
            _temporaryWavePath = wavePath;
            _reader = new WaveFileReader(wavePath);
            _output = new WaveOutEvent { DesiredLatency = 100 };
            _output.Init(_reader);
            _volume = volume;
            _output.Volume = _isMuted ? 0f : _volume;
            _timelineOffset = target;

            if (_resumeAfterSeek && target < _duration)
                _output.Play();
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
                Interlocked.Exchange(ref _isSeeking, 0);
        }
    }

    /// <summary>
    /// Keeps the independently-clocked audio output close to LibVLC's video
    /// clock. A drift correction rebuilds the AAC decoder from the video time.
    /// </summary>
    public async Task SynchroniseAsync(TimeSpan videoPosition)
    {
        if (!IsPlaying || _reader is null || IsSeeking)
            return;

        if (Math.Abs((Position - videoPosition).TotalMilliseconds) > 500)
            await SeekAsync(videoPosition, resumePlayback: true)
                .ConfigureAwait(false);
    }

    private static string? DecodeToWaveFile(
        string filePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        Mp4MediaInfo mediaInfo = new Mp4FileReader().Read(stream);

        Mp4MediaTrack? audioTrack = mediaInfo
            .GetTracks(Mp4HandlerType.Audio)
            .FirstOrDefault();

        if (audioTrack is null)
            return null;

        bool hasAacDescription = audioTrack.Track.SampleDescriptions.Any(
            description => description.Audio is
            {
                Codec: Mp4AudioCodec.Aac,
                AudioSpecificConfig: not null
            });

        if (!hasAacDescription)
            return null;

        AacMp4AudioTrackDecoder decoder = new();
        string wavePath = Path.Combine(
            Path.GetTempPath(),
            $"PixoraStudio_AAC_{Guid.NewGuid():N}.wav");

        WaveFileWriter? writer = null;
        PcmAudioFormat? format = null;
        long writtenFrames = 0;

        Dictionary<uint, Mp4PresentationSample> presentationBySampleNumber =
            mediaInfo.PresentationSamples
                .Where(sample => sample.TrackId == audioTrack.TrackId)
                .GroupBy(sample => sample.SampleNumber)
                .ToDictionary(group => group.Key, group => group.First());

        try
        {
            // Decode every audio sample in decode order. Samples excluded by
            // an edit list still need to warm the AAC overlap-add state.
            foreach (Mp4SampleLocation location in audioTrack.SampleLocations.Samples)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (location.SampleDescriptionIndex == 0 ||
                    location.SampleDescriptionIndex > audioTrack.Track.SampleDescriptions.Count)
                {
                    throw new InvalidDataException(
                        $"AAC sample {location.SampleNumber} references an invalid sample description.");
                }

                Mp4SampleDescription description =
                    audioTrack.Track.SampleDescriptions[
                        checked((int)location.SampleDescriptionIndex - 1)];

                if (description.Audio?.Codec != Mp4AudioCodec.Aac)
                    throw new NotSupportedException(
                        "The selected MP4 audio track changes to a non-AAC sample description.");

                Mp4EncodedSample encodedSample = ReadEncodedSample(
                    stream,
                    location,
                    mediaInfo.MediaDataRegions);

                PcmAudioBuffer decoded = decoder.DecodeSample(
                    encodedSample,
                    description);

                // Keep decoding preroll samples to preserve synthesis state,
                // but only write samples that appear on the presentation timeline.
                if (!presentationBySampleNumber.TryGetValue(
                        location.SampleNumber,
                        out Mp4PresentationSample presentationSample))
                {
                    continue;
                }

                format ??= decoded.Format;

                if (decoded.Format != format)
                    throw new InvalidDataException(
                        "The AAC track changed PCM format while it was being decoded.");

                if (writer is null)
                {
                    if (format.ChannelCount is < 1 or > 2)
                        throw new NotSupportedException(
                            "Custom AAC playback currently supports mono and stereo audio.");

                    writer = new WaveFileWriter(
                        wavePath,
                        new WaveFormat(format.SampleRate, 16, format.ChannelCount));
                }

                long sampleStartFrame = checked((long)Math.Round(
                    presentationSample.PresentationTimeSeconds * format.SampleRate,
                    MidpointRounding.AwayFromZero));

                PcmAudioTimelinePlacementResult placement =
                    PcmAudioTimelinePlacement.Calculate(
                        sampleStartFrame,
                        writtenFrames,
                        decoded.FrameCount);

                if (placement.SilenceFrames > 0)
                {
                    WriteSilence(
                        writer,
                        placement.SilenceFrames,
                        format.ChannelCount);
                }

                if (placement.FramesToWrite > 0)
                {
                    WriteBuffer(
                        writer,
                        decoded,
                        format,
                        placement.FramesToSkip);
                }

                writtenFrames = placement.ResultingWrittenFrames;
            }

            writer?.Dispose();
            writer = null;

            if (format is null || !File.Exists(wavePath) ||
                new FileInfo(wavePath).Length <= 44)
            {
                if (File.Exists(wavePath))
                    File.Delete(wavePath);

                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return wavePath;
        }
        catch
        {
            writer?.Dispose();

            if (File.Exists(wavePath))
                File.Delete(wavePath);

            throw;
        }
    }

    private static string? DecodeSeekToWaveFile(
        string filePath,
        double targetPresentationTimeSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        Mp4MediaInfo mediaInfo = new Mp4FileReader().Read(stream);
        Mp4MediaTrack? audioTrack = mediaInfo
            .GetTracks(Mp4HandlerType.Audio)
            .FirstOrDefault();

        if (audioTrack is null)
            return null;

        PcmAudioBuffer decoded = new AacMp4AudioSeekDecoder().Decode(
            stream,
            mediaInfo,
            audioTrack.TrackId,
            targetPresentationTimeSeconds,
            cancellationToken);

        if (decoded.FrameCount == 0)
            return null;

        if (decoded.Format.ChannelCount is < 1 or > 2)
            throw new NotSupportedException(
                "Custom AAC playback currently supports mono and stereo audio.");

        string wavePath = Path.Combine(
            Path.GetTempPath(),
            $"PixoraStudio_AAC_Seek_{Guid.NewGuid():N}.wav");

        try
        {
            using WaveFileWriter writer = new(
                wavePath,
                new WaveFormat(
                    decoded.Format.SampleRate,
                    16,
                    decoded.Format.ChannelCount));

            WriteBuffer(writer, decoded, decoded.Format, 0);
            cancellationToken.ThrowIfCancellationRequested();
            return wavePath;
        }
        catch
        {
            if (File.Exists(wavePath))
                File.Delete(wavePath);
            throw;
        }
    }

    private static Mp4EncodedSample ReadEncodedSample(
        Stream stream,
        Mp4SampleLocation location,
        IReadOnlyList<Mp4MediaData> mediaDataRegions)
    {
        if (location.Size > int.MaxValue)
            throw new InvalidDataException("The AAC sample is too large to decode.");

        if (!mediaDataRegions.Any(region =>
                region.Contains(location.Offset, location.Size)))
        {
            throw new InvalidDataException(
                $"AAC sample {location.SampleNumber} is outside every mdat region.");
        }

        if (location.Offset > long.MaxValue ||
            location.Offset > (ulong)stream.Length ||
            location.Size > (ulong)stream.Length - location.Offset)
        {
            throw new InvalidDataException(
                $"AAC sample {location.SampleNumber} extends beyond the MP4 stream.");
        }

        byte[] data = new byte[checked((int)location.Size)];
        stream.Position = checked((long)location.Offset);
        stream.ReadExactly(data);

        return new Mp4EncodedSample(location, data);
    }

    private static void WriteSilence(
        WaveFileWriter writer,
        long frameCount,
        int channelCount)
    {
        int bytesPerFrame = checked(channelCount * sizeof(short));
        byte[] silence = new byte[16 * 1024];
        int chunkSize = silence.Length - (silence.Length % bytesPerFrame);

        while (frameCount > 0)
        {
            int frames = (int)Math.Min(
                frameCount,
                chunkSize / bytesPerFrame);

            int bytes = frames * bytesPerFrame;
            writer.Write(silence, 0, bytes);
            frameCount -= frames;
        }
    }

    private static void WriteBuffer(
        WaveFileWriter writer,
        PcmAudioBuffer buffer,
        PcmAudioFormat expectedFormat,
        int framesToSkip)
    {
        if (buffer.Format != expectedFormat)
            throw new InvalidDataException(
                "The AAC track changed PCM format while it was being decoded.");

        PcmAudioBufferReader reader = new(buffer);
        if (framesToSkip > 0)
        {
            reader.SeekToFrame(framesToSkip);
        }

        byte[] bytes = new byte[16 * 1024];

        while (true)
        {
            int bytesRead = reader.Read(bytes, 0, bytes.Length);

            if (bytesRead == 0)
                break;

            writer.Write(bytes, 0, bytesRead);
        }
    }

    private void StopAndRelease()
    {
        try
        {
            _output?.Stop();
        }
        finally
        {
            _output?.Dispose();
            _output = null;

            _reader?.Dispose();
            _reader = null;

            if (_temporaryWavePath is not null)
            {
                try
                {
                    if (File.Exists(_temporaryWavePath))
                        File.Delete(_temporaryWavePath);
                }
                catch (IOException)
                {
                    // The OS may briefly retain the file after the audio device stops.
                }

                _temporaryWavePath = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Interlocked.Increment(ref _loadGeneration);
        _seekCancellation?.Cancel();
        _seekCancellation?.Dispose();
        _seekCancellation = null;
        StopAndRelease();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

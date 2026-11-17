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
    private bool _disposed;
    private int _loadGeneration;

    public bool IsLoaded => _reader is not null && _output is not null;

    public bool IsPlaying =>
        _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Position =>
        _reader?.CurrentTime ?? TimeSpan.Zero;

    public TimeSpan Duration =>
        _reader?.TotalTime ?? TimeSpan.Zero;

    public float Volume
    {
        get => _output?.Volume ?? 1f;
        set
        {
            if (_output is not null)
                _output.Volume = Math.Clamp(value, 0f, 1f);
        }
    }

    public async Task<bool> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        int generation = Interlocked.Increment(ref _loadGeneration);
        StopAndRelease();

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
            _output.Volume = 1f;
            return true;
        }
        catch
        {
            StopAndRelease();
            throw;
        }
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_output is null || _reader is null)
            return;

        if (_reader.Position >= _reader.Length)
            _reader.Position = 0;

        _output.Play();
    }

    public void Pause()
    {
        if (!_disposed)
            _output?.Pause();
    }

    public void Unload()
    {
        Interlocked.Increment(ref _loadGeneration);
        if (!_disposed)
            StopAndRelease();
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _output?.Stop();

        if (_reader is not null)
            _reader.Position = 0;
    }

    public void Seek(TimeSpan position, bool resumePlayback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_reader is null || _output is null)
            return;

        TimeSpan target = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > _reader.TotalTime
                ? _reader.TotalTime
                : position;

        _output.Stop();
        _reader.CurrentTime = target;

        if (resumePlayback && target < _reader.TotalTime)
            _output.Play();
    }

    /// <summary>
    /// Keeps the independently-clocked audio output close to LibVLC's video
    /// clock. Small differences are left alone to avoid unnecessary restarts.
    /// </summary>
    public void Synchronise(TimeSpan videoPosition)
    {
        if (!IsPlaying || _reader is null)
            return;

        if (Math.Abs((Position - videoPosition).TotalMilliseconds) > 500)
            Seek(videoPosition, resumePlayback: true);
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

        try
        {
            foreach (Mp4PresentationSample presentationSample
                     in mediaInfo.PresentationSamples.Where(
                         sample => sample.TrackId == audioTrack.TrackId))
            {
                cancellationToken.ThrowIfCancellationRequested();

                Mp4SampleLocation location = presentationSample.Location;

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

                int framesToSkip = 0;

                if (sampleStartFrame < 0)
                {
                    framesToSkip = (int)Math.Min(
                        decoded.FrameCount,
                        -sampleStartFrame);
                    sampleStartFrame = 0;
                }

                if (sampleStartFrame > writtenFrames)
                {
                    WriteSilence(
                        writer,
                        sampleStartFrame - writtenFrames,
                        format.ChannelCount);

                    writtenFrames = sampleStartFrame;
                }

                if (sampleStartFrame < writtenFrames)
                {
                    long overlap = writtenFrames - sampleStartFrame;
                    framesToSkip += (int)Math.Min(
                        decoded.FrameCount - framesToSkip,
                        overlap);
                }

                if (framesToSkip < decoded.FrameCount)
                {
                    WriteBuffer(writer, decoded, format, framesToSkip);
                    writtenFrames += decoded.FrameCount - framesToSkip;
                }
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
            reader.Seek(TimeSpan.FromSeconds(
                (double)framesToSkip / expectedFormat.SampleRate));
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
        StopAndRelease();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

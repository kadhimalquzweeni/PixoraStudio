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

        StopAndRelease();

        if (!File.Exists(filePath) ||
            !string.Equals(Path.GetExtension(filePath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? wavePath = await Task.Run(
            () => DecodeToWaveFile(filePath, cancellationToken),
            cancellationToken).ConfigureAwait(false);

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
            description => description.Audio?.Codec == Mp4AudioCodec.Aac &&
                           description.Audio.AudioSpecificConfig is not null);

        if (!hasAacDescription)
            return null;

        AacMp4AudioTrackDecoder decoder = new();

        using IEnumerator<PcmAudioBuffer> samples = decoder
            .DecodeSamples(
                stream,
                audioTrack,
                mediaInfo.MediaDataRegions,
                cancellationToken)
            .GetEnumerator();

        if (!samples.MoveNext())
            return null;

        PcmAudioBuffer firstBuffer = samples.Current;
        PcmAudioFormat format = firstBuffer.Format;

        if (format.ChannelCount is < 1 or > 2)
            throw new NotSupportedException(
                "Custom AAC playback currently supports mono and stereo audio.");

        string wavePath = Path.Combine(
            Path.GetTempPath(),
            $"PixoraStudio_AAC_{Guid.NewGuid():N}.wav");

        try
        {
            WaveFormat waveFormat = new(
                format.SampleRate,
                16,
                format.ChannelCount);

            using (WaveFileWriter writer = new(wavePath, waveFormat))
            {
                WriteBuffer(writer, firstBuffer, format);

                while (samples.MoveNext())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteBuffer(writer, samples.Current, format);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (new FileInfo(wavePath).Length <= 44)
            {
                File.Delete(wavePath);
                return null;
            }

            return wavePath;
        }
        catch
        {
            if (File.Exists(wavePath))
                File.Delete(wavePath);

            throw;
        }
    }

    private static void WriteBuffer(
        WaveFileWriter writer,
        PcmAudioBuffer buffer,
        PcmAudioFormat expectedFormat)
    {
        if (buffer.Format != expectedFormat)
            throw new InvalidDataException(
                "The AAC track changed PCM format while it was being decoded.");

        PcmAudioBufferReader reader = new(buffer);
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

        StopAndRelease();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

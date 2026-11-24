using System.Buffers.Binary;

namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Reads an interleaved floating-point PCM buffer as little-endian signed
/// 16-bit PCM, suitable for standard Windows audio output devices.
/// </summary>
public sealed class PcmAudioBufferReader
{
    private readonly PcmAudioBuffer _buffer;
    private int _framePosition;

    public PcmAudioFormat Format => _buffer.Format;

    public int BytesPerFrame => checked(Format.ChannelCount * sizeof(short));

    public long PositionBytes => (long)_framePosition * BytesPerFrame;

    public long LengthBytes => (long)_buffer.FrameCount * BytesPerFrame;

    public TimeSpan Position =>
        TimeSpan.FromSeconds((double)_framePosition / Format.SampleRate);

    public PcmAudioBufferReader(PcmAudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> bytes. Reads only complete PCM
    /// frames so callers never receive a partial interleaved frame.
    /// </summary>
    public int Read(byte[] destination, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (offset < 0 || offset > destination.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (count < 0 || count > destination.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(count));

        int requestedFrames = count / BytesPerFrame;
        int availableFrames = _buffer.FrameCount - _framePosition;
        int framesToRead = Math.Min(requestedFrames, availableFrames);

        if (framesToRead == 0)
            return 0;

        ReadOnlySpan<float> samples = _buffer.Samples.Span;
        int sampleIndex = checked(_framePosition * Format.ChannelCount);
        int destinationIndex = offset;

        for (int frame = 0; frame < framesToRead; frame++)
        {
            for (int channel = 0; channel < Format.ChannelCount; channel++)
            {
                float sample = samples[sampleIndex++];
                if (float.IsNaN(sample))
                    sample = 0f;
                sample = Math.Clamp(sample, -1f, 1f);
                int scaled = (int)MathF.Round(sample * (sample < 0 ? 32768f : 32767f));
                short pcmSample = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);

                BinaryPrimitives.WriteInt16LittleEndian(
                    destination.AsSpan(destinationIndex, sizeof(short)),
                    pcmSample);

                destinationIndex += sizeof(short);
            }
        }

        _framePosition += framesToRead;
        return framesToRead * BytesPerFrame;
    }

    public void Seek(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(position));

        double requestedFrame = position.TotalSeconds * Format.SampleRate;
        _framePosition = requestedFrame >= _buffer.FrameCount
            ? _buffer.FrameCount
            : (int)requestedFrame;
    }

    public void SeekToFrame(int frameIndex)
    {
        if (frameIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(frameIndex));

        _framePosition = Math.Min(frameIndex, _buffer.FrameCount);
    }

    public void Rewind()
    {
        _framePosition = 0;
    }
}

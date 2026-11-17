
namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Stores interleaved floating-point PCM audio samples.
/// </summary>
public sealed class PcmAudioBuffer
{
    private readonly float[] _samples;

    public PcmAudioFormat Format { get; }

    public ReadOnlyMemory<float> Samples => _samples;

    public int FrameCount =>
        _samples.Length / Format.ChannelCount;

    public int SampleCount => _samples.Length;

    public double DurationSeconds =>
        Format.GetDurationSeconds(FrameCount);

    public PcmAudioBuffer(
        PcmAudioFormat format,
        ReadOnlySpan<float> samples)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (samples.Length % format.ChannelCount != 0)
        {
            throw new ArgumentException(
                "The sample count must be divisible by the channel count.",
                nameof(samples));
        }

        Format = format;
        _samples = samples.ToArray();
    }

    public float GetSample(
        int frameIndex,
        int channelIndex)
    {
        if ((uint)frameIndex >= (uint)FrameCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameIndex));
        }

        if ((uint)channelIndex >= (uint)Format.ChannelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelIndex));
        }

        int sampleIndex =
            (frameIndex * Format.ChannelCount) + channelIndex;

        return _samples[sampleIndex];
    }

    public float[] ToArray()
    {
        return _samples.ToArray();
    }
}

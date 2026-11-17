
namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Describes the format of interleaved floating-point PCM audio.
/// </summary>
public sealed record PcmAudioFormat
{
    public int SampleRate { get; }

    public int ChannelCount { get; }

    public PcmAudioFormat(
        int sampleRate,
        int channelCount)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate));
        }

        if (channelCount is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelCount));
        }

        SampleRate = sampleRate;
        ChannelCount = channelCount;
    }

    public double GetDurationSeconds(int frameCount)
    {
        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameCount));
        }

        return (double)frameCount / SampleRate;
    }
}

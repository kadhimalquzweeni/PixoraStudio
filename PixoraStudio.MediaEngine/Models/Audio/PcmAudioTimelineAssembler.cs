namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Assembles decoded PCM blocks into one continuous sample-frame timeline.
/// Gaps are filled with silence and overlapping frames are discarded.
/// </summary>
public sealed class PcmAudioTimelineAssembler
{
    private readonly PcmAudioFormat _format;
    private readonly List<float> _samples = [];

    public long WrittenFrames =>
        _samples.Count / _format.ChannelCount;

    public PcmAudioTimelineAssembler(PcmAudioFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        _format = format;
    }

    /// <summary>
    /// Places a decoded block at an absolute presentation frame.
    /// Negative starts are treated as decoder preroll and trimmed.
    /// </summary>
    public void Append(
        PcmAudioBuffer buffer,
        long presentationStartFrame)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer.Format != _format)
        {
            throw new ArgumentException(
                "The PCM block format does not match the timeline format.",
                nameof(buffer));
        }

        PcmAudioTimelinePlacementResult placement =
            PcmAudioTimelinePlacement.Calculate(
                presentationStartFrame,
                WrittenFrames,
                buffer.FrameCount);

        int silenceSamples = checked(
            (int)(placement.SilenceFrames * _format.ChannelCount));

        if (silenceSamples > 0)
        {
            _samples.AddRange(new float[silenceSamples]);
        }

        int sourceSampleOffset = checked(
            placement.FramesToSkip * _format.ChannelCount);
        int samplesToWrite = checked(
            placement.FramesToWrite * _format.ChannelCount);

        if (samplesToWrite > 0)
        {
            _samples.AddRange(
                buffer.Samples.Span
                    .Slice(sourceSampleOffset, samplesToWrite)
                    .ToArray());
        }

        if (WrittenFrames != placement.ResultingWrittenFrames)
        {
            throw new InvalidOperationException(
                "The assembled PCM timeline length does not match its calculated placement.");
        }
    }

    public PcmAudioBuffer Build()
    {
        return new PcmAudioBuffer(_format, _samples.ToArray());
    }

    public void Reset()
    {
        _samples.Clear();
    }
}

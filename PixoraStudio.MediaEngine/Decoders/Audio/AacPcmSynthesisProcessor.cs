using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Converts one decoded AAC-LC long-window spectrum into a mono PCM frame.
/// State must be retained between consecutive frames to preserve overlap.
/// Short-window and transition sequences are rejected until their dedicated
/// transition-window assembly is available.
/// </summary>
public sealed class AacPcmSynthesisProcessor
{
    private readonly AacImdct _imdct = new();
    private readonly AacSynthesisOverlapAdd _overlapAdd = new();

    public PcmAudioBuffer Process(
        ReadOnlySpan<float> spectrum,
        AacIndividualChannelStreamInfo streamInfo,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (spectrum.Length != AacImdct.LongSpectrumLength)
            throw new ArgumentException(
                "An AAC-LC spectrum must contain 1024 coefficients.",
                nameof(spectrum));

        if (streamInfo.WindowSequence != AacWindowSequence.OnlyLongSequence)
            throw new NotSupportedException(
                "PCM synthesis currently supports ONLY_LONG_SEQUENCE. " +
                "Long-start, eight-short and long-stop transition windows are not yet supported.");

        float[] timeSamples = _imdct.Transform(spectrum);
        float[] window = AacWindowFunction.Create(streamInfo.WindowShape);

        for (int i = 0; i < timeSamples.Length; i++)
            timeSamples[i] *= window[i];

        float[] frame = _overlapAdd.Process(timeSamples);
        return new PcmAudioBuffer(new PcmAudioFormat(sampleRate, 1), frame);
    }

    public void Reset() => _overlapAdd.Reset();
}

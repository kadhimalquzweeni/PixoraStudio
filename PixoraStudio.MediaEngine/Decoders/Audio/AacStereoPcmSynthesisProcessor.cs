using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Synthesises two AAC-LC channel spectra into interleaved stereo PCM.
/// Keep this processor for the lifetime of the stereo stream.
/// </summary>
public sealed class AacStereoPcmSynthesisProcessor
{
    private readonly AacPcmSynthesisProcessor _left = new();
    private readonly AacPcmSynthesisProcessor _right = new();

    public PcmAudioBuffer Process(
        ReadOnlySpan<float> leftSpectrum,
        AacIndividualChannelStreamInfo leftInfo,
        ReadOnlySpan<float> rightSpectrum,
        AacIndividualChannelStreamInfo rightInfo,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(leftInfo);
        ArgumentNullException.ThrowIfNull(rightInfo);

        PcmAudioBuffer left = _left.Process(leftSpectrum, leftInfo, sampleRate);
        PcmAudioBuffer right = _right.Process(rightSpectrum, rightInfo, sampleRate);

        if (left.FrameCount != right.FrameCount)
            throw new InvalidDataException("Stereo channel synthesis produced different frame lengths.");

        float[] interleaved = new float[checked(left.FrameCount * 2)];
        ReadOnlySpan<float> leftSamples = left.Samples.Span;
        ReadOnlySpan<float> rightSamples = right.Samples.Span;

        for (int frame = 0; frame < left.FrameCount; frame++)
        {
            interleaved[frame * 2] = leftSamples[frame];
            interleaved[(frame * 2) + 1] = rightSamples[frame];
        }

        return new PcmAudioBuffer(
            new PcmAudioFormat(sampleRate, 2),
            interleaved);
    }

    public void Reset()
    {
        _left.Reset();
        _right.Reset();
    }
}

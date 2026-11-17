using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Converts AAC-LC spectral frames into mono PCM using IMDCT, sequence-aware
/// windowing and overlap-add. Keep one instance per audio channel.
/// </summary>
public sealed class AacPcmSynthesisProcessor
{
    private const int LongLength = AacImdct.LongSpectrumLength;
    private const int ShortLength = AacImdct.ShortSpectrumLength;
    private const int ShortStart = (LongLength - ShortLength) / 2;
    private const int ShortWindowCount = 8;

    private readonly AacImdct _imdct = new();
    private readonly AacSynthesisOverlapAdd _overlapAdd = new();
    private AacWindowShape _previousWindowShape = AacWindowShape.Sine;
    private bool _hasPreviousFrame;

    public PcmAudioBuffer Process(
        ReadOnlySpan<float> spectrum,
        AacIndividualChannelStreamInfo streamInfo,
        int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(streamInfo);

        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (spectrum.Length != LongLength)
            throw new ArgumentException(
                "An AAC-LC spectrum must contain 1024 coefficients.",
                nameof(spectrum));

        float[] timeBlock = streamInfo.WindowSequence switch
        {
            AacWindowSequence.OnlyLongSequence =>
                CreateLongWindowBlock(spectrum, streamInfo.WindowShape),
            AacWindowSequence.LongStartSequence =>
                CreateLongStartBlock(spectrum, streamInfo.WindowShape),
            AacWindowSequence.EightShortSequence =>
                CreateEightShortBlock(spectrum, streamInfo.WindowShape),
            AacWindowSequence.LongStopSequence =>
                CreateLongStopBlock(spectrum, streamInfo.WindowShape),
            _ => throw new ArgumentOutOfRangeException(nameof(streamInfo))
        };

        float[] frame = _overlapAdd.Process(timeBlock);
        _previousWindowShape = streamInfo.WindowShape;
        _hasPreviousFrame = true;

        return new PcmAudioBuffer(new PcmAudioFormat(sampleRate, 1), frame);
    }

    public void Reset()
    {
        _overlapAdd.Reset();
        _previousWindowShape = AacWindowShape.Sine;
        _hasPreviousFrame = false;
    }

    private float[] CreateLongWindowBlock(
        ReadOnlySpan<float> spectrum,
        AacWindowShape currentShape)
    {
        float[] transformed = _imdct.Transform(spectrum);
        float[] previousWindow = AacWindowFunction.Create(
            _hasPreviousFrame ? _previousWindowShape : AacWindowShape.Sine);
        float[] currentWindow = AacWindowFunction.Create(currentShape);

        ApplyWindowHalves(transformed, previousWindow, currentWindow);
        return transformed;
    }

    private float[] CreateLongStartBlock(
        ReadOnlySpan<float> spectrum,
        AacWindowShape currentShape)
    {
        float[] transformed = _imdct.Transform(spectrum);
        float[] longWindow = AacWindowFunction.Create(
            _hasPreviousFrame ? _previousWindowShape : AacWindowShape.Sine);
        float[] shortWindow = AacWindowFunction.Create(currentShape, shortWindow: true);
        float[] result = new float[AacImdct.LongOutputLength];

        // The left half remains a long window.
        for (int i = 0; i < LongLength; i++)
            result[i] = transformed[i] * longWindow[i];

        // The right half transitions through a flat region and the right
        // half of a short window, then becomes zero.
        Array.Copy(transformed, LongLength, result, LongLength, 448);
        for (int i = 0; i < ShortLength / 2; i++)
            result[LongLength + 448 + i] =
                transformed[LongLength + 448 + i] * shortWindow[ShortLength / 2 + i];

        return result;
    }

    private float[] CreateLongStopBlock(
        ReadOnlySpan<float> spectrum,
        AacWindowShape currentShape)
    {
        float[] transformed = _imdct.Transform(spectrum);
        float[] longWindow = AacWindowFunction.Create(currentShape);
        float[] shortWindow = AacWindowFunction.Create(
            _hasPreviousFrame ? _previousWindowShape : AacWindowShape.Sine,
            shortWindow: true);
        float[] result = new float[AacImdct.LongOutputLength];

        // The first half transitions from zero through the left half of a
        // short window into a flat region.
        for (int i = 0; i < ShortLength / 2; i++)
            result[ShortStart + i] =
                transformed[ShortStart + i] * shortWindow[i];

        Array.Copy(transformed, ShortStart + ShortLength / 2,
            result, ShortStart + ShortLength / 2, 448);

        // The right half returns to a long window.
        for (int i = LongLength; i < AacImdct.LongOutputLength; i++)
            result[i] = transformed[i] * longWindow[i];

        return result;
    }

    private float[] CreateEightShortBlock(
        ReadOnlySpan<float> spectrum,
        AacWindowShape shape)
    {
        float[] shortWindow = AacWindowFunction.Create(shape, shortWindow: true);
        float[] result = new float[AacImdct.LongOutputLength];

        for (int window = 0; window < ShortWindowCount; window++)
        {
            ReadOnlySpan<float> coefficients =
                spectrum.Slice(window * ShortLength, ShortLength);
            float[] transformed = _imdct.Transform(coefficients);
            int destinationStart = ShortStart + (window * ShortLength / 2);

            for (int i = 0; i < transformed.Length; i++)
                result[destinationStart + i] += transformed[i] * shortWindow[i];
        }

        return result;
    }

    private static void ApplyWindowHalves(
        float[] samples,
        float[] previousWindow,
        float[] currentWindow)
    {
        for (int i = 0; i < LongLength; i++)
            samples[i] *= previousWindow[i];

        for (int i = LongLength; i < samples.Length; i++)
            samples[i] *= currentWindow[i];
    }
}

using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Decodes AAC-LC mono access units from an already parsed MP4 audio track.
/// MP4 AAC samples contain raw_data_block payloads, not ADTS headers.
/// </summary>
public sealed class AacMp4AudioTrackDecoder
{
    private readonly AacRawDataBlockReader _rawDataBlockReader = new();
    private readonly AacIndividualChannelStreamProcessor _channelProcessor;
    private readonly AacPcmSynthesisProcessor _synthesisProcessor = new();

    public AacMp4AudioTrackDecoder(IAacNoiseSource? noiseSource = null)
    {
        _channelProcessor = new AacIndividualChannelStreamProcessor(noiseSource);
    }

    public PcmAudioBuffer DecodeSample(
        Mp4EncodedSample sample,
        Mp4SampleDescription sampleDescription)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(sampleDescription);

        Mp4AudioSampleEntry audioEntry = sampleDescription.Audio
            ?? throw new InvalidDataException(
                "The MP4 sample description does not contain an audio sample entry.");

        Mp4AudioSpecificConfig audioConfig = audioEntry.AudioSpecificConfig
            ?? throw new InvalidDataException(
                "The MP4 audio sample entry has no AudioSpecificConfig.");

        AacAudioDecoderConfiguration configuration =
            AacAudioDecoderConfiguration.Create(audioConfig);

        if (configuration.ChannelCount != 1)
        {
            throw new NotSupportedException(
                "The MP4 AAC decoder currently supports mono channel configuration only.");
        }

        AacSingleChannelElement element =
            _rawDataBlockReader.ReadSingleChannelElement(
                sample.Data,
                configuration.SamplingFrequencyIndex);

        float[] spectrum = _channelProcessor.Process(
            element.ChannelStream,
            configuration.SamplingFrequencyIndex);

        return _synthesisProcessor.Process(
            spectrum,
            element.ChannelStream.StreamInfo,
            configuration.SampleRate);
    }

    /// <summary>
    /// Decodes samples in MP4 decode order. The supplied stream must remain
    /// open while the returned sequence is being enumerated.
    /// </summary>
    public IEnumerable<PcmAudioBuffer> DecodeSamples(
        Stream stream,
        Mp4MediaTrack track,
        IReadOnlyList<Mp4MediaData> mediaDataRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(mediaDataRegions);

        if (track.HandlerType != Mp4HandlerType.Audio)
            throw new ArgumentException("The selected MP4 track is not an audio track.", nameof(track));

        Reset();

        foreach (Mp4EncodedSample sample in track.ReadSamples(
                     stream,
                     mediaDataRegions,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            uint descriptionIndex = sample.Location.SampleDescriptionIndex;
            if (descriptionIndex == 0 || descriptionIndex > track.Track.SampleDescriptions.Count)
                throw new InvalidDataException(
                    $"Sample {sample.Location.SampleNumber} references an invalid sample description.");

            Mp4SampleDescription description =
                track.Track.SampleDescriptions[checked((int)descriptionIndex - 1)];

            yield return DecodeSample(sample, description);
        }
    }

    public void Reset()
    {
        _synthesisProcessor.Reset();
    }
}

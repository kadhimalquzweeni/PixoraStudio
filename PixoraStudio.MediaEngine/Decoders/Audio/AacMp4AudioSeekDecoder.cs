using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Timeline;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Decodes an AAC MP4 track from a requested presentation time, rebuilding
/// overlap state with decode-order preroll and assembling output on the
/// presentation timeline.
/// </summary>
public sealed class AacMp4AudioSeekDecoder
{
    private readonly Mp4AudioSeekPlanner _seekPlanner = new();

    public PcmAudioBuffer Decode(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        double targetPresentationTimeSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);

        Mp4MediaTrack track = mediaInfo.GetTrack(trackId)
            ?? throw new ArgumentOutOfRangeException(
                nameof(trackId), trackId, "The MP4 track does not exist.");

        if (track.HandlerType != Mp4HandlerType.Audio)
            throw new ArgumentException(
                "The selected MP4 track is not an audio track.",
                nameof(trackId));

        Mp4SampleDescription firstAacDescription = track.Track.SampleDescriptions
            .FirstOrDefault(description =>
                description.Audio is
                {
                    Codec: Mp4AudioCodec.Aac,
                    AudioSpecificConfig: not null
                })
            ?? throw new NotSupportedException(
                "The selected audio track does not contain an AAC sample description.");

        int sampleRate = firstAacDescription.Audio!.AudioSpecificConfig!.SampleRate;

        Mp4AudioSeekPlan plan = _seekPlanner.Plan(
            track,
            mediaInfo.PresentationSamples,
            targetPresentationTimeSeconds,
            sampleRate);

        Dictionary<uint, Mp4PresentationSample> presentationBySample =
            mediaInfo.PresentationSamples
                .Where(sample => sample.TrackId == trackId)
                .GroupBy(sample => sample.SampleNumber)
                .ToDictionary(group => group.Key, group => group.First());

        long targetFrame = checked((long)Math.Round(
            targetPresentationTimeSeconds * sampleRate,
            MidpointRounding.AwayFromZero));

        AacMp4AudioTrackDecoder decoder = new();
        PcmAudioTimelineAssembler? assembler = null;

        uint samplesToDecode = track.SampleCount - plan.FirstSampleToDecode + 1;

        foreach (Mp4EncodedSample encoded in track.ReadSamples(
                     stream,
                     plan.FirstSampleToDecode,
                     samplesToDecode,
                     mediaInfo.MediaDataRegions,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            Mp4SampleDescription description = GetSampleDescription(
                track,
                encoded.Location.SampleDescriptionIndex,
                encoded.Location.SampleNumber);

            PcmAudioBuffer decoded = decoder.DecodeSample(encoded, description);

            // Decode preroll to restore AAC overlap-add state, but do not emit
            // PCM before the requested target sample.
            if (encoded.Location.SampleNumber < plan.TargetSampleNumber)
                continue;

            assembler ??= new PcmAudioTimelineAssembler(decoded.Format);

            if (encoded.Location.SampleNumber == plan.TargetSampleNumber &&
                plan.FramesToSkipInTargetSample > 0)
            {
                int framesToSkip = Math.Min(
                    plan.FramesToSkipInTargetSample,
                    decoded.FrameCount);

                if (framesToSkip == decoded.FrameCount)
                    continue;

                int firstSample = checked(
                    framesToSkip * decoded.Format.ChannelCount);

                decoded = new PcmAudioBuffer(
                    decoded.Format,
                    decoded.Samples.Span[firstSample..]);
            }

            // Edit-list-excluded samples still get decoded above so they can
            // maintain synthesis state, but are not placed on the output timeline.
            if (!presentationBySample.TryGetValue(
                    encoded.Location.SampleNumber,
                    out Mp4PresentationSample presentation))
            {
                continue;
            }

            long sampleStartFrame = checked((long)Math.Round(
                presentation.PresentationTimeSeconds * sampleRate,
                MidpointRounding.AwayFromZero)) - targetFrame;

            if (encoded.Location.SampleNumber == plan.TargetSampleNumber)
                sampleStartFrame = checked(
                    sampleStartFrame + plan.FramesToSkipInTargetSample);

            assembler.Append(decoded, sampleStartFrame);
        }

        if (assembler is null || assembler.WrittenFrames == 0)
            return new PcmAudioBuffer(
                new PcmAudioFormat(sampleRate, firstAacDescription.Audio.ChannelCount),
                ReadOnlySpan<float>.Empty);

        return assembler.Build();
    }

    private static Mp4SampleDescription GetSampleDescription(
        Mp4MediaTrack track,
        uint descriptionIndex,
        uint sampleNumber)
    {
        if (descriptionIndex == 0 ||
            descriptionIndex > track.Track.SampleDescriptions.Count)
        {
            throw new InvalidDataException(
                $"AAC sample {sampleNumber} references an invalid sample description.");
        }

        Mp4SampleDescription description =
            track.Track.SampleDescriptions[checked((int)descriptionIndex - 1)];

        if (description.Audio?.Codec != Mp4AudioCodec.Aac)
            throw new NotSupportedException(
                "The selected MP4 audio track changes to a non-AAC sample description.");

        return description;
    }

}

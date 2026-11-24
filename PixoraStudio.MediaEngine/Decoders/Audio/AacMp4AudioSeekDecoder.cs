using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.Audio;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Timeline;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Decodes AAC MP4 samples from a requested presentation time. Decode-order
/// preroll restores overlap state while yielded PCM is positioned on the
/// presentation timeline.
/// </summary>
public sealed class AacMp4AudioSeekDecoder
{
    private readonly Mp4AudioSeekPlanner _seekPlanner = new();

    /// <summary>
    /// Decodes the remaining presentation audio into one PCM buffer. This
    /// convenience API is intended for tests and bounded in-memory workloads.
    /// Playback code should consume <see cref="DecodeSamples"/> incrementally.
    /// </summary>
    public PcmAudioBuffer Decode(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        double targetPresentationTimeSeconds,
        CancellationToken cancellationToken = default)
    {
        Mp4MediaTrack track = GetAudioTrack(mediaInfo, trackId);
        Mp4SampleDescription firstDescription = GetFirstAacDescription(track);
        int sampleRate = checked((int)firstDescription.Audio!.AudioSpecificConfig!.SamplingFrequency);
        PcmAudioTimelineAssembler? assembler = null;

        foreach (Mp4DecodedAudioSample sample in DecodeSamples(
                     stream,
                     mediaInfo,
                     trackId,
                     targetPresentationTimeSeconds,
                     cancellationToken))
        {
            assembler ??= new PcmAudioTimelineAssembler(sample.Buffer.Format);
            assembler.Append(sample.Buffer, sample.PresentationStartFrame);
        }

        return assembler?.Build() ?? new PcmAudioBuffer(
            new PcmAudioFormat(sampleRate, firstDescription.Audio!.ChannelCount),
            ReadOnlySpan<float>.Empty);
    }

    /// <summary>
    /// Decodes samples in decode order and yields only samples on or after the
    /// requested presentation time. Each yielded buffer is already trimmed at
    /// the seek point and carries its presentation start frame relative to it.
    /// The stream must remain open while the sequence is enumerated.
    /// </summary>
    public IEnumerable<Mp4DecodedAudioSample> DecodeSamples(
        Stream stream,
        Mp4MediaInfo mediaInfo,
        uint trackId,
        double targetPresentationTimeSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mediaInfo);

        Mp4MediaTrack track = GetAudioTrack(mediaInfo, trackId);
        Mp4SampleDescription firstDescription = GetFirstAacDescription(track);
        int sampleRate = checked((int)firstDescription.Audio!.AudioSpecificConfig!.SamplingFrequency);

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

            // Edit-list-excluded samples are still decoded to maintain AAC
            // synthesis state, but are not emitted on the presentation timeline.
            if (!presentationBySample.TryGetValue(
                    encoded.Location.SampleNumber,
                    out Mp4PresentationSample? presentation) ||
                presentation is null)
            {
                continue;
            }

            long sampleStartFrame = checked((long)Math.Round(
                presentation.PresentationTimeSeconds * sampleRate,
                MidpointRounding.AwayFromZero)) - targetFrame;

            if (encoded.Location.SampleNumber == plan.TargetSampleNumber)
                sampleStartFrame = checked(
                    sampleStartFrame + plan.FramesToSkipInTargetSample);

            yield return new Mp4DecodedAudioSample(
                encoded.Location.SampleNumber,
                sampleStartFrame,
                decoded);
        }
    }

    private static Mp4MediaTrack GetAudioTrack(
        Mp4MediaInfo mediaInfo,
        uint trackId)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        Mp4MediaTrack track = mediaInfo.GetTrack(trackId)
            ?? throw new ArgumentOutOfRangeException(
                nameof(trackId), trackId, "The MP4 track does not exist.");

        if (track.HandlerType != Mp4HandlerType.Audio)
            throw new ArgumentException(
                "The selected MP4 track is not an audio track.",
                nameof(trackId));

        return track;
    }

    private static Mp4SampleDescription GetFirstAacDescription(
        Mp4MediaTrack track)
    {
        return track.Track.SampleDescriptions
            .FirstOrDefault(description =>
                description.Audio is
                {
                    Codec: Mp4AudioCodec.Aac,
                    AudioSpecificConfig: not null
                })
            ?? throw new NotSupportedException(
                "The selected audio track does not contain an AAC sample description.");
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

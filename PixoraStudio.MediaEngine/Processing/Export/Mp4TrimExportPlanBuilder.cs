using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Creates a safe, keyframe-aligned sample selection for managed MP4 remuxing.
/// This planner deliberately selects complete video GOPs; it never starts on a P/B frame.
/// </summary>
public sealed class Mp4TrimExportPlanBuilder
{
    private const double TimeToleranceSeconds = 0.000001;

    public Mp4TrimExportPlan Build(
        Mp4MediaInfo mediaInfo,
        double startSeconds,
        double endSeconds)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);

        if (!double.IsFinite(startSeconds) || startSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Trim start must be a finite, non-negative time.");

        if (!double.IsFinite(endSeconds) || endSeconds <= startSeconds)
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "Trim end must be greater than trim start.");

        if (endSeconds > mediaInfo.DurationSeconds + TimeToleranceSeconds)
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "Trim end exceeds the movie duration.");

        Mp4MediaTrack videoTrack = mediaInfo.GetTracks(Mp4HandlerType.Video).FirstOrDefault()
            ?? throw new NotSupportedException("The MP4 file does not contain a video track.");

        if (videoTrack.EditList is not null)
            throw new NotSupportedException("Source video edit lists are not yet supported by managed trim export.");

        Dictionary<uint, double> videoPresentationTimes = GetPresentationTimes(mediaInfo, videoTrack.TrackId);
        Mp4SampleLocation[] videoSamples = videoTrack.SampleLocations.Samples.ToArray();

        Mp4SampleLocation? startSync = videoSamples
            .Where(sample => sample.IsSyncSample && videoPresentationTimes.ContainsKey(sample.SampleNumber))
            .Where(sample => videoPresentationTimes[sample.SampleNumber] <= startSeconds + TimeToleranceSeconds)
            .OrderByDescending(sample => videoPresentationTimes[sample.SampleNumber])
            .ThenByDescending(sample => sample.SampleNumber)
            .FirstOrDefault();

        startSync ??= videoSamples
            .Where(sample => sample.IsSyncSample && videoPresentationTimes.ContainsKey(sample.SampleNumber))
            .OrderBy(sample => videoPresentationTimes[sample.SampleNumber])
            .ThenBy(sample => sample.SampleNumber)
            .FirstOrDefault();

        if (startSync is null)
            throw new NotSupportedException("The video track has no usable sync sample for a decodable trim start.");

        // End at the next sync sample at or beyond the requested end. The next
        // sync sample is an exclusive boundary, so the preceding GOP is complete.
        Mp4SampleLocation? endBoundary = videoSamples
            .Where(sample => sample.IsSyncSample && sample.SampleNumber > startSync.SampleNumber)
            .Where(sample => videoPresentationTimes.TryGetValue(sample.SampleNumber, out double time)
                && time + TimeToleranceSeconds >= endSeconds)
            .OrderBy(sample => sample.SampleNumber)
            .FirstOrDefault();

        uint lastVideoSampleNumber = endBoundary is null
            ? videoSamples[^1].SampleNumber
            : endBoundary.SampleNumber - 1;

        if (lastVideoSampleNumber < startSync.SampleNumber)
            throw new NotSupportedException("The selected range does not contain a complete decodable video GOP.");

        Mp4SampleLocation[] selectedVideoSamples = videoSamples
            .Where(sample => sample.SampleNumber >= startSync.SampleNumber &&
                             sample.SampleNumber <= lastVideoSampleNumber)
            .ToArray();

        if (selectedVideoSamples.Length == 0 ||
            selectedVideoSamples[0].SampleNumber != startSync.SampleNumber ||
            selectedVideoSamples[^1].SampleNumber != lastVideoSampleNumber)
            throw new Mp4ParsingException("The video sample table is not contiguous across the selected GOP range.");

        double effectiveStart = videoPresentationTimes[startSync.SampleNumber];
        double effectiveEnd = endBoundary is not null
            ? videoPresentationTimes[endBoundary.SampleNumber]
            : GetTrackEndSeconds(videoTrack, selectedVideoSamples, videoPresentationTimes);

        if (effectiveEnd <= effectiveStart)
            throw new NotSupportedException("The selected video GOP range has no positive presentation duration.");

        List<Mp4TrimTrackPlan> trackPlans =
        [
            new Mp4TrimTrackPlan(videoTrack.TrackId, Mp4HandlerType.Video, selectedVideoSamples)
        ];

        foreach (Mp4MediaTrack audioTrack in mediaInfo.GetTracks(Mp4HandlerType.Audio))
        {
            if (audioTrack.EditList is not null)
                throw new NotSupportedException("Source audio edit lists are not yet supported by managed trim export.");

            Dictionary<uint, double> audioTimes = GetPresentationTimes(mediaInfo, audioTrack.TrackId);
            Mp4SampleLocation[] selectedAudioSamples = audioTrack.SampleLocations.Samples
                .Where(sample => audioTimes.TryGetValue(sample.SampleNumber, out double time)
                    && time < effectiveEnd
                    && time + sample.DurationTicks / (double)audioTrack.Timescale > effectiveStart - TimeToleranceSeconds)
                .ToArray();

            if (selectedAudioSamples.Length == 0)
                continue;

            // Audio is selected in decode order. MP4 tracks have contiguous sample
            // numbering; if edit lists produce gaps, fail instead of writing invalid tables.
            for (int i = 1; i < selectedAudioSamples.Length; i++)
            {
                if (selectedAudioSamples[i].SampleNumber != selectedAudioSamples[i - 1].SampleNumber + 1)
                    throw new NotSupportedException("Audio samples selected across a discontinuous edit list are not supported by this exporter.");
            }

            trackPlans.Add(new Mp4TrimTrackPlan(audioTrack.TrackId, Mp4HandlerType.Audio, selectedAudioSamples));
        }

        return new Mp4TrimExportPlan(
            startSeconds,
            endSeconds,
            effectiveStart,
            effectiveEnd,
            trackPlans);
    }

    private static Dictionary<uint, double> GetPresentationTimes(Mp4MediaInfo mediaInfo, uint trackId) =>
        mediaInfo.PresentationSamples
            .Where(sample => sample.TrackId == trackId)
            .GroupBy(sample => sample.SampleNumber)
            .ToDictionary(group => group.Key, group => group.First().PresentationTimeSeconds);

    private static double GetTrackEndSeconds(
        Mp4MediaTrack track,
        IReadOnlyList<Mp4SampleLocation> selectedSamples,
        IReadOnlyDictionary<uint, double> presentationTimes)
    {
        Mp4SampleLocation last = selectedSamples[^1];
        double start = presentationTimes.TryGetValue(last.SampleNumber, out double time)
            ? time
            : last.DecodeTimeTicks / (double)track.Timescale;

        return start + last.DurationTicks / (double)track.Timescale;
    }
}

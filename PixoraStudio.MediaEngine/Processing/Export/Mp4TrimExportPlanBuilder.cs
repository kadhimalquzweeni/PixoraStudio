using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Builds a frame-accurate MP4 trim plan. The selected video samples begin at a
/// decodable sync sample and include decode preroll, while the output edit list
/// hides frames outside the requested frame-aligned presentation interval.
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

        IReadOnlyList<Mp4MediaTrack> videoTracks = mediaInfo.GetTracks(Mp4HandlerType.Video);
        if (videoTracks.Count == 0)
            throw new NotSupportedException("The MP4 file does not contain a video track.");
        if (videoTracks.Count > 1)
            throw new NotSupportedException("Managed trim export currently supports one video track.");

        Mp4MediaTrack videoTrack = videoTracks[0];
        if (videoTrack.EditList is not null)
            throw new NotSupportedException("Source video edit lists are not yet supported by managed trim export.");

        Dictionary<uint, double> videoPresentationTimes = GetPresentationTimes(mediaInfo, videoTrack.TrackId);
        Mp4SampleLocation[] videoSamples = videoTrack.SampleLocations.Samples.ToArray();
        if (videoSamples.Length == 0)
            throw new NotSupportedException("The video track contains no samples.");

        // Resolve UI times to actual frame boundaries. Presentation timestamps,
        // rather than decode order, are authoritative for B-frame streams.
        Mp4SampleLocation startFrame = videoSamples
            .Where(sample => videoPresentationTimes.ContainsKey(sample.SampleNumber))
            .OrderBy(sample => videoPresentationTimes[sample.SampleNumber])
            .ThenBy(sample => sample.SampleNumber)
            .FirstOrDefault(sample =>
                videoPresentationTimes[sample.SampleNumber] + TimeToleranceSeconds >= startSeconds)
            ?? throw new ArgumentOutOfRangeException(nameof(startSeconds), "No video frame begins at or after the requested trim start.");

        Mp4SampleLocation? endFrame = videoSamples
            .Where(sample => videoPresentationTimes.ContainsKey(sample.SampleNumber))
            .OrderBy(sample => videoPresentationTimes[sample.SampleNumber])
            .ThenBy(sample => sample.SampleNumber)
            .FirstOrDefault(sample =>
                videoPresentationTimes[sample.SampleNumber] + TimeToleranceSeconds >= endSeconds);

        double effectiveStart = videoPresentationTimes[startFrame.SampleNumber];
        double effectiveEnd = endFrame is not null
            ? videoPresentationTimes[endFrame.SampleNumber]
            : GetTrackEndSeconds(videoTrack, videoSamples, videoPresentationTimes);

        if (effectiveEnd <= effectiveStart)
            throw new NotSupportedException("The selected frame range has no positive presentation duration.");

        // Decode from the closest preceding sync sample. If the requested
        // interval begins before the first sync sample, start at that sync
        // sample because earlier frames cannot be decoded independently.
        Mp4SampleLocation? startSync = videoSamples
            .Where(sample => sample.IsSyncSample && videoPresentationTimes.ContainsKey(sample.SampleNumber))
            .Where(sample => sample.SampleNumber <= startFrame.SampleNumber &&
                videoPresentationTimes[sample.SampleNumber] <= effectiveStart + TimeToleranceSeconds)
            .OrderByDescending(sample => videoPresentationTimes[sample.SampleNumber])
            .ThenByDescending(sample => sample.SampleNumber)
            .FirstOrDefault();

        startSync ??= videoSamples
            .Where(sample => sample.IsSyncSample && videoPresentationTimes.ContainsKey(sample.SampleNumber))
            .Where(sample => sample.SampleNumber >= startFrame.SampleNumber)
            .OrderBy(sample => videoPresentationTimes[sample.SampleNumber])
            .ThenBy(sample => sample.SampleNumber)
            .FirstOrDefault();

        if (startSync is null)
            throw new NotSupportedException("The video track has no usable sync sample for a decodable trim start.");

        if (videoPresentationTimes[startSync.SampleNumber] > effectiveStart + TimeToleranceSeconds)
        {
            // The requested first frame precedes the first available random
            // access point; align the visible start to the first decodable frame.
            effectiveStart = videoPresentationTimes[startSync.SampleNumber];
            if (effectiveEnd <= effectiveStart)
                throw new NotSupportedException("The selected range ends before the first decodable video frame.");
        }

        // Retain complete decode-order GOPs through the first sync sample at or
        // after the exclusive end boundary. The edit list hides the preroll and
        // any trailing GOP samples that are not part of the visible interval.
        Mp4SampleLocation? endBoundary = videoSamples
            .Where(sample => sample.IsSyncSample && sample.SampleNumber > startSync.SampleNumber)
            .Where(sample => videoPresentationTimes.TryGetValue(sample.SampleNumber, out double time)
                && time + TimeToleranceSeconds >= effectiveEnd)
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
                    && time < effectiveEnd - TimeToleranceSeconds
                    && time + sample.DurationTicks / (double)audioTrack.Timescale > effectiveStart + TimeToleranceSeconds)
                .ToArray();

            if (selectedAudioSamples.Length == 0)
                continue;

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
        Mp4SampleLocation last = selectedSamples
            .OrderBy(sample => presentationTimes.TryGetValue(sample.SampleNumber, out double time)
                ? time
                : sample.DecodeTimeTicks / (double)track.Timescale)
            .ThenBy(sample => sample.SampleNumber)
            .Last();

        double start = presentationTimes.TryGetValue(last.SampleNumber, out double time)
            ? time
            : last.DecodeTimeTicks / (double)track.Timescale;

        return start + last.DurationTicks / (double)track.Timescale;
    }
}

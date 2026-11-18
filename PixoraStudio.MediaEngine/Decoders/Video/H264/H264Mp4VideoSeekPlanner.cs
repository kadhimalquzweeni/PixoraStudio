using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Maps a presentation timestamp to a decode-order random-access sample.
/// H.264 pictures after that sample are decoded continuously so the DPB and
/// POC state are rebuilt before the requested presentation interval is emitted.
/// </summary>
public sealed class H264Mp4VideoSeekPlanner
{
    public H264Mp4VideoSeekPlan Plan(
        Mp4MediaTrack track,
        double targetPresentationTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (track.HandlerType != Mp4HandlerType.Video)
            throw new ArgumentException("The selected track is not a video track.", nameof(track));
        if (track.Timescale == 0)
            throw new FormatException("The MP4 video track timescale must be greater than zero.");
        if (!double.IsFinite(targetPresentationTimeSeconds) ||
            targetPresentationTimeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(targetPresentationTimeSeconds));
        if (track.SampleCount == 0)
            throw new FormatException("The MP4 video track contains no samples.");

        long requestedTicks;
        try
        {
            decimal ticks = (decimal)targetPresentationTimeSeconds * track.Timescale;
            requestedTicks = checked((long)decimal.Floor(ticks));
        }
        catch (OverflowException exception)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPresentationTimeSeconds),
                targetPresentationTimeSeconds,
                $"The requested seek time exceeds the MP4 track clock range: {exception.Message}");
        }

        Mp4SampleLocation[] samples = track.SampleLocations.Samples.ToArray();
        if ((uint)samples.Length != track.SampleCount)
            throw new FormatException("The MP4 video sample-location count is inconsistent.");

        long presentationEnd = long.MinValue;
        foreach (Mp4SampleLocation sample in samples)
        {
            if (sample.SampleNumber == 0 ||
                sample.SampleNumber > track.SampleCount ||
                sample.DurationTicks == 0)
                throw new FormatException("The MP4 video track contains invalid sample timing.");

            long end;
            try
            {
                end = checked(sample.PresentationTimeTicks + sample.DurationTicks);
            }
            catch (OverflowException exception)
            {
                throw new FormatException("An MP4 video sample presentation interval overflows.", exception);
            }

            presentationEnd = Math.Max(presentationEnd, end);
        }

        // Seeking beyond the media clamps to the final representable tick of
        // the presentation timeline, allowing the final picture to be shown.
        long targetTicks = Math.Min(requestedTicks, presentationEnd - 1);

        Mp4SampleLocation[] randomAccessSamples = samples
            .Where(sample => sample.IsSyncSample)
            .ToArray();
        if (randomAccessSamples.Length == 0)
            throw new NotSupportedException(
                "The MP4 video track has no sync sample from which H.264 decoding can safely begin.");

        Mp4SampleLocation? precedingSync = randomAccessSamples
            .Where(sample => sample.PresentationTimeTicks <= targetTicks)
            .OrderByDescending(sample => sample.PresentationTimeTicks)
            .ThenByDescending(sample => sample.SampleNumber)
            .FirstOrDefault();

        // A target before the first sync picture must still start at a valid
        // random-access point; output filtering handles the requested time.
        Mp4SampleLocation selectedSync = precedingSync ??
            randomAccessSamples
                .OrderBy(sample => sample.PresentationTimeTicks)
                .ThenBy(sample => sample.SampleNumber)
                .First();

        return new H264Mp4VideoSeekPlan(
            selectedSync.SampleNumber,
            targetTicks,
            track.Timescale);
    }
}

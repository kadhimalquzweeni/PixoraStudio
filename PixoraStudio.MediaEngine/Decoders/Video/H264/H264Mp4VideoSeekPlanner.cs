using PixoraStudio.MediaEngine.Models.General;
using System.Runtime.CompilerServices;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Maps presentation timestamps to decode-order random-access samples.
/// Track timing is indexed once and reused for subsequent seek requests.
/// </summary>
public sealed class H264Mp4VideoSeekPlanner
{
    private readonly ConditionalWeakTable<Mp4MediaTrack, SeekIndex> _indexes = new();

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

        SeekIndex index = _indexes.GetValue(track, static key => BuildIndex(key));
        long targetTicks = Math.Min(requestedTicks, index.PresentationEndTicks - 1);
        uint firstSample = FindPrecedingSyncSample(index.SyncSamples, targetTicks);

        return new H264Mp4VideoSeekPlan(firstSample, targetTicks, track.Timescale);
    }

    private static SeekIndex BuildIndex(Mp4MediaTrack track)
    {
        Mp4SampleLocation[] samples = track.SampleLocations.Samples.ToArray();
        if ((uint)samples.Length != track.SampleCount)
            throw new FormatException("The MP4 video sample-location count is inconsistent.");
        if (samples.Length == 0)
            throw new FormatException("The MP4 video track contains no samples.");

        long presentationEnd = long.MinValue;
        List<SyncSample> syncSamples = [];

        for (int index = 0; index < samples.Length; index++)
        {
            Mp4SampleLocation sample = samples[index];
            if (sample.SampleNumber != checked((uint)index + 1) ||
                sample.DurationTicks == 0)
                throw new FormatException(
                    "The MP4 video track contains invalid or non-contiguous sample timing.");

            long end;
            try
            {
                end = checked(sample.PresentationTimeTicks + sample.DurationTicks);
            }
            catch (OverflowException exception)
            {
                throw new FormatException(
                    "An MP4 video sample presentation interval overflows.", exception);
            }

            presentationEnd = Math.Max(presentationEnd, end);
            if (sample.IsSyncSample)
                syncSamples.Add(new SyncSample(sample.PresentationTimeTicks, sample.SampleNumber));
        }

        if (syncSamples.Count == 0)
            throw new NotSupportedException(
                "The MP4 video track has no sync sample from which H.264 decoding can safely begin.");

        // Composition offsets can make presentation order differ from decode order.
        // Sort once and binary-search this index for every subsequent thumbnail.
        syncSamples.Sort(static (left, right) =>
        {
            int comparison = left.PresentationTimeTicks.CompareTo(right.PresentationTimeTicks);
            return comparison != 0 ? comparison : left.SampleNumber.CompareTo(right.SampleNumber);
        });

        return new SeekIndex(presentationEnd, [.. syncSamples]);
    }

    private static uint FindPrecedingSyncSample(
        SyncSample[] syncSamples,
        long targetPresentationTimeTicks)
    {
        int low = 0;
        int high = syncSamples.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (syncSamples[middle].PresentationTimeTicks <= targetPresentationTimeTicks)
                low = middle + 1;
            else
                high = middle;
        }

        if (low == 0)
            return syncSamples[0].SampleNumber;

        long selectedTimestamp = syncSamples[low - 1].PresentationTimeTicks;
        int firstAtTimestamp = low - 1;
        while (firstAtTimestamp > 0 &&
               syncSamples[firstAtTimestamp - 1].PresentationTimeTicks == selectedTimestamp)
            firstAtTimestamp--;

        // Match the original tie-break: choose the lowest sample number for equal PTS.
        return syncSamples[firstAtTimestamp].SampleNumber;
    }

    private sealed record SeekIndex(long PresentationEndTicks, SyncSample[] SyncSamples);

    private readonly record struct SyncSample(long PresentationTimeTicks, uint SampleNumber);
}

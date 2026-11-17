using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Timeline;

/// <summary>
/// Finds the encoded audio sample needed to resume playback at a presentation
/// timestamp. AAC synthesis has overlap state, so the planner includes one
/// preceding decode-order sample as preroll.
/// </summary>
public sealed class Mp4AudioSeekPlanner
{
    public Mp4AudioSeekPlan Plan(
        Mp4MediaTrack track,
        IEnumerable<Mp4PresentationSample> presentationSamples,
        double targetPresentationTimeSeconds,
        int sampleRate,
        int framesPerAccessUnit = 1024)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(presentationSamples);

        if (track.HandlerType != Mp4HandlerType.Audio)
        {
            throw new ArgumentException(
                "The selected track is not an audio track.",
                nameof(track));
        }

        if (track.Timescale == 0)
        {
            throw new Mp4ParsingException(
                "The audio track timescale must be greater than zero.");
        }

        if (!double.IsFinite(targetPresentationTimeSeconds) ||
            targetPresentationTimeSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPresentationTimeSeconds));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (framesPerAccessUnit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerAccessUnit));
        }

        Mp4PresentationSample[] samples = presentationSamples
            .Where(sample => sample.TrackId == track.TrackId)
            .OrderBy(sample => sample.PresentationTimeSeconds)
            .ThenBy(sample => sample.SampleNumber)
            .ToArray();

        if (samples.Length == 0)
        {
            throw new Mp4ParsingException(
                "The audio track has no presentation samples.");
        }

        Mp4PresentationSample? selected = null;
        bool isAtEnd = false;

        foreach (Mp4PresentationSample sample in samples)
        {
            double sampleDuration =
                (double)sample.Location.DurationTicks / track.Timescale;
            double sampleEnd =
                sample.PresentationTimeSeconds + sampleDuration;

            if (targetPresentationTimeSeconds >= sample.PresentationTimeSeconds &&
                targetPresentationTimeSeconds < sampleEnd)
            {
                selected = sample;
                break;
            }

            if (sample.PresentationTimeSeconds > targetPresentationTimeSeconds)
            {
                selected = sample;
                break;
            }
        }

        if (selected is null)
        {
            selected = samples[^1];
            isAtEnd = true;
        }

        double offsetSeconds = Math.Max(
            0,
            targetPresentationTimeSeconds - selected.PresentationTimeSeconds);

        long requestedSkip = (long)Math.Floor(offsetSeconds * sampleRate);
        int framesToSkip = (int)Math.Clamp(
            requestedSkip,
            0,
            framesPerAccessUnit);

        uint firstSampleToDecode = selected.SampleNumber > 1
            ? selected.SampleNumber - 1
            : selected.SampleNumber;

        return new Mp4AudioSeekPlan(
            firstSampleToDecode,
            selected.SampleNumber,
            framesToSkip,
            targetPresentationTimeSeconds,
            selected.PresentationTimeSeconds,
            isAtEnd);
    }
}

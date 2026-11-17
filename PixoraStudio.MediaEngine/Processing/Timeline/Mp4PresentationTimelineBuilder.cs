using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Timeline;

/// <summary>
/// Builds a unified presentation timeline from multiple MP4 tracks.
/// </summary>
public sealed class Mp4PresentationTimelineBuilder
{
    public IReadOnlyList<Mp4PresentationSample> Build(
        Mp4MovieHeader movieHeader,
        IReadOnlyList<Mp4MediaTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(movieHeader);
        ArgumentNullException.ThrowIfNull(tracks);

        if (movieHeader.Timescale == 0)
        {
            throw new Mp4ParsingException(
                "The movie timescale must be greater than zero.");
        }

        List<Mp4PresentationSample> result = [];

        foreach (Mp4MediaTrack track in tracks)
        {
            if (track.Timescale == 0)
            {
                throw new Mp4ParsingException(
                    $"Track {track.TrackId} has an invalid timescale.");
            }

            if (track.EditList is null)
            {
                AddUneditedTrackSamples(result, track);
            }
            else
            {
                AddEditedTrackSamples(
                    result,
                    movieHeader,
                    track);
            }
        }

        return result
            .OrderBy(sample => sample.PresentationTimeSeconds)
            .ThenBy(sample => sample.TrackId)
            .ThenBy(sample => sample.SampleNumber)
            .ToArray();
    }

    private static void AddUneditedTrackSamples(
        List<Mp4PresentationSample> result,
        Mp4MediaTrack track)
    {
        foreach (Mp4SampleLocation location
                 in track.SampleLocations.Samples)
        {
            double presentationTime =
                (double)location.PresentationTimeTicks /
                track.Timescale;

            result.Add(
                new Mp4PresentationSample(
                    track.TrackId,
                    location.SampleNumber,
                    location,
                    presentationTime));
        }
    }

    private static void AddEditedTrackSamples(
        List<Mp4PresentationSample> result,
        Mp4MovieHeader movieHeader,
        Mp4MediaTrack track)
    {
        double presentationCursor = 0;

        foreach (Mp4EditListEntry edit
                 in track.EditList!.Entries)
        {
            double segmentDurationSeconds =
                (double)edit.SegmentDuration /
                movieHeader.Timescale;

            if (edit.IsEmptyEdit)
            {
                presentationCursor += segmentDurationSeconds;
                continue;
            }

            double mediaStart = edit.MediaTime;

            double mediaEnd =
                mediaStart +
                (segmentDurationSeconds * track.Timescale);

            foreach (Mp4SampleLocation location
                     in track.SampleLocations.Samples)
            {
                long presentationTicks =
                    location.PresentationTimeTicks;

                if (presentationTicks < mediaStart ||
                    presentationTicks >= mediaEnd)
                {
                    continue;
                }

                double mediaOffsetSeconds =
                    (presentationTicks - mediaStart) /
                    track.Timescale;

                double presentationTime =
                    presentationCursor + mediaOffsetSeconds;

                result.Add(
                    new Mp4PresentationSample(
                        track.TrackId,
                        location.SampleNumber,
                        location,
                        presentationTime));
            }

            presentationCursor += segmentDurationSeconds;
        }
    }
}
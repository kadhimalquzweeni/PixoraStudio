using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Rebuilds a moov box for a trim plan, retaining sample descriptions and
/// unrelated metadata while replacing timing, edit-list and sample-table data.
/// </summary>
public sealed class Mp4TrimMoovWriter
{
    private readonly Mp4BoxWriter _boxWriter = new();
    private readonly Mp4BoxTreeWriter _treeWriter = new();
    private readonly Mp4DurationBoxWriter _durationWriter = new();
    private readonly Mp4SampleTableBoxWriter _sampleTableWriter = new();
    private readonly Mp4EditListBoxWriter _editListWriter = new();
    private readonly Mp4TrackHeaderReader _trackHeaderReader = new();

    public void Write(
        Stream source,
        Stream destination,
        Mp4Box moovBox,
        Mp4MediaInfo mediaInfo,
        Mp4TrimExportPlan exportPlan,
        IReadOnlyDictionary<uint, IReadOnlyList<ulong>> chunkOffsetsByTrack,
        bool force64BitChunkOffsets = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(moovBox);
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(exportPlan);
        ArgumentNullException.ThrowIfNull(chunkOffsetsByTrack);

        if (moovBox.Type != "moov")
            throw new ArgumentException("Expected a moov box.", nameof(moovBox));

        if (moovBox.Children.Any(box => box.Type == "mvex"))
            throw new NotSupportedException("Fragmented MP4 files are not supported by the trim exporter.");

        ulong movieDuration = ToTicks(
            exportPlan.EffectiveEndSeconds - exportPlan.EffectiveStartSeconds,
            mediaInfo.MovieHeader.Timescale);

        Dictionary<uint, Mp4TrimTrackPlan> plans = exportPlan.Tracks
            .ToDictionary(track => track.TrackId);

        if (plans.Count != chunkOffsetsByTrack.Count)
            throw new ArgumentException("Chunk offsets do not match the export track plans.", nameof(chunkOffsetsByTrack));

        int movieHeaderCount = moovBox.Children.Count(box => box.Type == "mvhd");
        if (movieHeaderCount != 1)
            throw new Mp4ParsingException("The moov box must contain exactly one mvhd box.");

        _boxWriter.WriteBox(destination, "moov", payload =>
        {
            foreach (Mp4Box child in moovBox.Children)
            {
                if (child.Type == "mvhd")
                {
                    _durationWriter.Write(source, payload, child, movieDuration);
                }
                else if (child.Type == "trak")
                {
                    WriteTrack(
                        source,
                        payload,
                        child,
                        mediaInfo,
                        exportPlan,
                        plans,
                        chunkOffsetsByTrack,
                        movieDuration,
                        force64BitChunkOffsets);
                }
                else
                {
                    _treeWriter.Write(source, payload, [child]);
                }
            }
        });
    }

    private void WriteTrack(
        Stream source,
        Stream destination,
        Mp4Box trackBox,
        Mp4MediaInfo mediaInfo,
        Mp4TrimExportPlan exportPlan,
        IReadOnlyDictionary<uint, Mp4TrimTrackPlan> plans,
        IReadOnlyDictionary<uint, IReadOnlyList<ulong>> chunkOffsetsByTrack,
        ulong movieDuration,
        bool force64BitChunkOffsets)
    {
        Mp4Box tkhd = trackBox.Children.SingleOrDefault(box => box.Type == "tkhd")
            ?? throw new Mp4ParsingException("A track is missing its tkhd box.");

        uint trackId = _trackHeaderReader.Read(source, tkhd).TrackId;

        if (!plans.TryGetValue(trackId, out Mp4TrimTrackPlan? plan))
            return;

        Mp4MediaTrack track = mediaInfo.GetTrack(trackId)
            ?? throw new Mp4ParsingException($"The export plan references unknown track {trackId}.");

        if (!chunkOffsetsByTrack.TryGetValue(trackId, out IReadOnlyList<ulong>? chunkOffsets) ||
            chunkOffsets.Count != plan.Samples.Count)
            throw new Mp4ParsingException($"Track {trackId} has incomplete output chunk offsets.");

        ulong mediaDuration = plan.Samples.Aggregate(
            0UL,
            (total, sample) => checked(total + sample.DurationTicks));

        IReadOnlyList<Mp4EditListEntry> edits = BuildEditList(
            track,
            plan,
            exportPlan,
            mediaInfo.MovieHeader.Timescale,
            movieDuration);

        _boxWriter.WriteBox(destination, "trak", trak =>
        {
            foreach (Mp4Box child in trackBox.Children)
            {
                if (child.Type == "tkhd")
                {
                    _durationWriter.Write(source, trak, child, movieDuration);
                    _editListWriter.Write(trak, edits);
                }
                else if (child.Type == "edts")
                {
                    // Replace any existing edit container with the normalized export edit list.
                }
                else if (child.Type == "mdia")
                {
                    WriteMediaBox(
                        source,
                        trak,
                        child,
                        plan,
                        chunkOffsets,
                        mediaDuration,
                        force64BitChunkOffsets);
                }
                else
                {
                    _treeWriter.Write(source, trak, [child]);
                }
            }
        });
    }

    private void WriteMediaBox(
        Stream source,
        Stream destination,
        Mp4Box mediaBox,
        Mp4TrimTrackPlan plan,
        IReadOnlyList<ulong> chunkOffsets,
        ulong mediaDuration,
        bool force64BitChunkOffsets)
    {
        _boxWriter.WriteBox(destination, "mdia", mdia =>
        {
            foreach (Mp4Box child in mediaBox.Children)
            {
                if (child.Type == "mdhd")
                    _durationWriter.Write(source, mdia, child, mediaDuration);
                else if (child.Type == "minf")
                    WriteMediaInformationBox(source, mdia, child, plan, chunkOffsets, force64BitChunkOffsets);
                else
                    _treeWriter.Write(source, mdia, [child]);
            }
        });
    }

    private void WriteMediaInformationBox(
        Stream source,
        Stream destination,
        Mp4Box mediaInformationBox,
        Mp4TrimTrackPlan plan,
        IReadOnlyList<ulong> chunkOffsets,
        bool force64BitChunkOffsets)
    {
        Mp4Box[] sampleTables = mediaInformationBox.Children
            .Where(box => box.Type == "stbl")
            .ToArray();
        if (sampleTables.Length != 1)
            throw new Mp4ParsingException($"Track {plan.TrackId} must contain exactly one stbl box.");

        _boxWriter.WriteBox(destination, "minf", minf =>
        {
            foreach (Mp4Box child in mediaInformationBox.Children)
            {
                if (child.Type != "stbl")
                {
                    _treeWriter.Write(source, minf, [child]);
                    continue;
                }

                string[] supportedSampleTableBoxes =
                [
                    "stsd", "stts", "stsc", "stsz", "stco", "co64", "stss", "ctts", "free"
                ];
                string? unsupportedBox = child.Children
                    .Select(box => box.Type)
                    .FirstOrDefault(type => !supportedSampleTableBoxes.Contains(type));
                if (unsupportedBox is not null)
                    throw new NotSupportedException($"Track {plan.TrackId} uses unsupported sample-table box '{unsupportedBox}'.");

                Mp4Box stsd = child.Children.SingleOrDefault(box => box.Type == "stsd")
                    ?? throw new Mp4ParsingException($"Track {plan.TrackId} is missing its stsd box.");

                _sampleTableWriter.Write(
                    minf,
                    plan,
                    chunkOffsets,
                    force64BitChunkOffsets,
                    source,
                    stsd);
            }
        });
    }

    private static IReadOnlyList<Mp4EditListEntry> BuildEditList(
        Mp4MediaTrack track,
        Mp4TrimTrackPlan plan,
        Mp4TrimExportPlan exportPlan,
        uint movieTimescale,
        ulong movieDuration)
    {
        Mp4SampleLocation first = plan.Samples[0];
        long targetMediaTime = checked(
            (long)ToTicks(exportPlan.EffectiveStartSeconds, track.Timescale) -
            checked((long)first.DecodeTimeTicks));

        if (plan.HandlerType == Mp4HandlerType.Video && targetMediaTime < 0)
            throw new NotSupportedException("The selected video sync sample has a negative composition-time origin.");

        if (targetMediaTime >= 0)
        {
            return
            [
                new Mp4EditListEntry(movieDuration, targetMediaTime, 1, 0)
            ];
        }

        ulong emptyEditDuration = ToTicks(
            (double)(-targetMediaTime) / track.Timescale,
            movieTimescale);

        if (emptyEditDuration >= movieDuration)
            throw new NotSupportedException("The selected track starts after the complete output duration.");

        return
        [
            new Mp4EditListEntry(emptyEditDuration, -1, 1, 0),
            new Mp4EditListEntry(movieDuration - emptyEditDuration, 0, 1, 0)
        ];
    }

    private static ulong ToTicks(double seconds, uint timescale)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || timescale == 0)
            throw new Mp4ParsingException("A trim duration cannot be represented in the selected track timescale.");

        decimal ticks = decimal.Round(
            (decimal)seconds * timescale,
            0,
            MidpointRounding.AwayFromZero);

        return checked((ulong)ticks);
    }
}

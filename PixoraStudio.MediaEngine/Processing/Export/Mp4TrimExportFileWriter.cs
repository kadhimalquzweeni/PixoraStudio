using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Creates a new non-fragmented MP4 by remuxing selected encoded samples.
/// No decoding, re-encoding, external executable, or shell process is used.
/// </summary>
public sealed class Mp4TrimExportFileWriter
{
    private const ulong ExtendedMdatHeaderSize = 16;

    private readonly Mp4Reader _boxReader = new();
    private readonly Mp4FileReader _fileReader = new();
    private readonly Mp4TrimExportPlanBuilder _planBuilder = new();
    private readonly Mp4TrimMoovWriter _moovWriter = new();
    private readonly Mp4MediaDataWriter _mediaDataWriter = new();
    private readonly Mp4BoxTreeWriter _treeWriter = new();

    public Mp4TrimExportPlan Export(
        Stream source,
        Stream destination,
        double startSeconds,
        double endSeconds,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (!source.CanRead || !source.CanSeek)
            throw new ArgumentException("The source MP4 stream must be readable and seekable.", nameof(source));
        if (!destination.CanWrite || !destination.CanSeek)
            throw new ArgumentException("The destination MP4 stream must be writable and seekable.", nameof(destination));
        if (ReferenceEquals(source, destination))
            throw new ArgumentException("Source and destination streams must be different.", nameof(destination));

        cancellationToken.ThrowIfCancellationRequested();

        Mp4MediaInfo sourceInfo = _fileReader.Read(source);
        progress?.Report(0.02);
        if (sourceInfo.Tracks.Any(track =>
            track.HandlerType is not Mp4HandlerType.Video and not Mp4HandlerType.Audio))
        {
            throw new NotSupportedException("The source MP4 contains tracks other than video/audio, which this exporter cannot preserve yet.");
        }

        Mp4TrimExportPlan plan = _planBuilder.Build(sourceInfo, startSeconds, endSeconds);
        progress?.Report(0.05);
        IReadOnlyList<Mp4Box> topLevelBoxes = _boxReader.ReadBoxTree(source);

        Mp4Box fileTypeBox = GetExactlyOne(topLevelBoxes, "ftyp");
        Mp4Box movieBox = GetExactlyOne(topLevelBoxes, "moov");

        if (topLevelBoxes.Any(box => box.Type is not "ftyp" and not "moov" and not "mdat" and not "free"))
            throw new NotSupportedException("The source MP4 contains top-level boxes that are not yet supported by the managed trim exporter.");

        if (!topLevelBoxes.Any(box => box.Type == "mdat"))
            throw new Mp4ParsingException("The source MP4 does not contain an mdat box.");

        destination.Position = 0;
        destination.SetLength(0);

        Dictionary<uint, IReadOnlyList<ulong>> placeholderOffsets = plan.Tracks
            .ToDictionary(
                track => track.TrackId,
                track => (IReadOnlyList<ulong>)Array.AsReadOnly(new ulong[track.Samples.Count]));

        using MemoryStream placeholderMoov = new();
        _moovWriter.Write(
            source,
            placeholderMoov,
            movieBox,
            sourceInfo,
            plan,
            placeholderOffsets,
            force64BitChunkOffsets: true);

        ulong outputMdatDataOffset = checked(
            (ulong)fileTypeBox.Size +
            (ulong)placeholderMoov.Length +
            ExtendedMdatHeaderSize);

        Dictionary<uint, IReadOnlyList<ulong>> outputOffsets =
            CalculateOutputOffsets(plan, outputMdatDataOffset);

        using MemoryStream outputMoov = new();
        _moovWriter.Write(
            source,
            outputMoov,
            movieBox,
            sourceInfo,
            plan,
            outputOffsets,
            force64BitChunkOffsets: true);

        if (outputMoov.Length != placeholderMoov.Length)
            throw new InvalidOperationException("The MP4 moov size changed while resolving output chunk offsets.");

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0.1);

        _treeWriter.Write(source, destination, [fileTypeBox]);
        outputMoov.Position = 0;
        outputMoov.CopyTo(destination);

        Mp4MediaDataWriteResult mediaDataResult = _mediaDataWriter.Write(
            source,
            destination,
            plan.Tracks,
            cancellationToken,
            progress is null ? null : new Progress<double>(value => progress.Report(0.1 + value * 0.8)));

        if (mediaDataResult.DataOffset != outputMdatDataOffset ||
            !OffsetsMatch(outputOffsets, mediaDataResult.ChunkOffsetsByTrack))
        {
            throw new InvalidOperationException("The written mdat layout does not match the rebuilt sample tables.");
        }

        progress?.Report(0.92);
        destination.Flush();
        destination.Position = 0;

        Mp4MediaInfo outputInfo = _fileReader.Read(destination);
        ValidateOutput(outputInfo, plan);
        progress?.Report(1);

        destination.Position = 0;
        return plan;
    }

    private static Dictionary<uint, IReadOnlyList<ulong>> CalculateOutputOffsets(
        Mp4TrimExportPlan plan,
        ulong firstDataOffset)
    {
        ulong currentOffset = firstDataOffset;
        Dictionary<uint, IReadOnlyList<ulong>> offsetsByTrack = [];

        foreach (Mp4TrimTrackPlan track in plan.Tracks)
        {
            List<ulong> offsets = new(track.Samples.Count);
            foreach (Mp4SampleLocation sample in track.Samples)
            {
                offsets.Add(currentOffset);
                currentOffset = checked(currentOffset + sample.Size);
            }

            offsetsByTrack.Add(track.TrackId, Array.AsReadOnly(offsets.ToArray()));
        }

        return offsetsByTrack;
    }

    private static bool OffsetsMatch(
        IReadOnlyDictionary<uint, IReadOnlyList<ulong>> expected,
        IReadOnlyDictionary<uint, IReadOnlyList<ulong>> actual)
    {
        if (expected.Count != actual.Count)
            return false;

        foreach ((uint trackId, IReadOnlyList<ulong> expectedOffsets) in expected)
        {
            if (!actual.TryGetValue(trackId, out IReadOnlyList<ulong>? actualOffsets) ||
                !expectedOffsets.SequenceEqual(actualOffsets))
                return false;
        }

        return true;
    }

    private static void ValidateOutput(
        Mp4MediaInfo output,
        Mp4TrimExportPlan plan)
    {
        if (output.Tracks.Count != plan.Tracks.Count)
            throw new Mp4ParsingException("The exported MP4 track count does not match the trim plan.");

        foreach (Mp4TrimTrackPlan plannedTrack in plan.Tracks)
        {
            Mp4MediaTrack outputTrack = output.GetTrack(plannedTrack.TrackId)
                ?? throw new Mp4ParsingException($"The exported MP4 is missing track {plannedTrack.TrackId}.");

            if (outputTrack.SampleCount != plannedTrack.Samples.Count)
                throw new Mp4ParsingException($"The exported track {plannedTrack.TrackId} has an unexpected sample count.");

            if (plannedTrack.HandlerType == Mp4HandlerType.Video &&
                !outputTrack.SampleLocations.GetSample(1).IsSyncSample)
            {
                throw new Mp4ParsingException("The exported video does not begin with a sync sample.");
            }
        }

        double expectedDuration = plan.EffectiveEndSeconds - plan.EffectiveStartSeconds;
        double durationTolerance = Math.Max(0.002, 1.0 / output.MovieHeader.Timescale + 0.000001);
        if (Math.Abs(output.DurationSeconds - expectedDuration) > durationTolerance)
            throw new Mp4ParsingException("The exported MP4 movie duration does not match the trim plan.");
    }

    private static Mp4Box GetExactlyOne(
        IReadOnlyList<Mp4Box> boxes,
        string type)
    {
        Mp4Box[] matches = boxes.Where(box => box.Type == type).ToArray();
        if (matches.Length != 1)
            throw new Mp4ParsingException($"Expected exactly one '{type}' top-level box.");

        return matches[0];
    }
}

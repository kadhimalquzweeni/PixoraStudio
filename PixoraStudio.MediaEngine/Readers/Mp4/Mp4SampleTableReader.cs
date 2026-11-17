using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Readers.Mp4;

/// <summary>
/// Reads all sample tables contained within an MP4 stbl box.
/// </summary>
public sealed class Mp4SampleTableReader
{
    private readonly Mp4TimeToSampleReader _timeToSampleReader = new();
    private readonly Mp4SampleToChunkReader _sampleToChunkReader = new();
    private readonly Mp4SampleSizeReader _sampleSizeReader = new();
    private readonly Mp4ChunkOffsetReader _chunkOffsetReader = new();
    private readonly Mp4SyncSampleReader _syncSampleReader = new();
    private readonly Mp4CompositionTimeReader _compositionTimeReader = new();

    public Mp4SampleTable Read(Stream stream, Mp4Box box)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(box);

        if (box.Type != "stbl")
        {
            throw new Mp4ParsingException(
                "Expected an stbl box.");
        }

        Mp4Box sttsBox = GetRequiredChild(box, "stts");
        Mp4Box stscBox = GetRequiredChild(box, "stsc");
        Mp4Box stszBox = GetRequiredChild(box, "stsz");

        Mp4Box chunkOffsetBox = GetChunkOffsetBox(box);
        Mp4Box? stssBox = GetOptionalChild(box, "stss");
        Mp4Box? cttsBox = GetOptionalChild(box, "ctts");

        IReadOnlyList<Mp4TimeToSampleEntry> timingEntries =
            _timeToSampleReader.Read(stream, sttsBox);

        IReadOnlyList<Mp4SampleToChunkEntry> chunkEntries =
            _sampleToChunkReader.Read(stream, stscBox);

        Mp4SampleSizeTable sampleSizes =
            _sampleSizeReader.Read(stream, stszBox);

        Mp4ChunkOffsetTable chunkOffsets =
            _chunkOffsetReader.Read(stream, chunkOffsetBox);

        Mp4SyncSampleTable? syncSamples = stssBox is null
            ? null
            : _syncSampleReader.Read(stream, stssBox);

        Mp4CompositionTimeTable? compositionTimes = cttsBox is null
            ? null
            : new Mp4CompositionTimeTable(
                _compositionTimeReader.Read(stream, cttsBox));

        return new Mp4SampleTable(
            timingEntries,
            chunkEntries,
            sampleSizes,
            chunkOffsets,
            syncSamples,
            compositionTimes);
    }

    private static Mp4Box GetRequiredChild(
        Mp4Box parent,
        string childType)
    {
        Mp4Box[] matches = parent.Children
            .Where(child => child.Type == childType)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new Mp4ParsingException(
                $"The stbl box does not contain a '{childType}' box.");
        }

        if (matches.Length > 1)
        {
            throw new Mp4ParsingException(
                $"The stbl box contains multiple '{childType}' boxes.");
        }

        return matches[0];
    }

    private static Mp4Box? GetOptionalChild(
        Mp4Box parent,
        string childType)
    {
        Mp4Box[] matches = parent.Children
            .Where(child => child.Type == childType)
            .ToArray();

        if (matches.Length > 1)
        {
            throw new Mp4ParsingException(
                $"The stbl box contains multiple '{childType}' boxes.");
        }

        return matches.Length == 0 ? null : matches[0];
    }

    private static Mp4Box GetChunkOffsetBox(Mp4Box parent)
    {
        Mp4Box? stco = GetOptionalChild(parent, "stco");
        Mp4Box? co64 = GetOptionalChild(parent, "co64");

        if (stco is not null && co64 is not null)
        {
            throw new Mp4ParsingException(
                "The stbl box cannot contain both stco and co64 boxes.");
        }

        return stco ?? co64 ??
            throw new Mp4ParsingException(
                "The stbl box does not contain an stco or co64 box.");
    }
}

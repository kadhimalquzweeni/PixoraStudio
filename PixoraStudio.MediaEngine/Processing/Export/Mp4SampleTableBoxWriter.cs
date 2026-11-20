using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.MediaEngine.Processing.Export;

/// <summary>
/// Serializes rebuilt MP4 sample-table boxes for a contiguous decode-order trim plan.
/// Chunk layout uses one sample per chunk to keep offsets deterministic.
/// </summary>
public sealed class Mp4SampleTableBoxWriter
{
    private readonly Mp4BoxWriter _boxWriter = new();
    private readonly Mp4BoxTreeWriter _boxTreeWriter = new();

    public void Write(
        Stream destination,
        Mp4TrimTrackPlan plan,
        IReadOnlyList<ulong> chunkOffsets,
        bool force64BitChunkOffsets = false,
        Stream? sourceStream = null,
        Mp4Box? sampleDescriptionBox = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(chunkOffsets);

        if (chunkOffsets.Count != plan.Samples.Count)
            throw new ArgumentException("There must be exactly one chunk offset per planned sample.", nameof(chunkOffsets));

        if (plan.Samples.Count == 0)
            throw new ArgumentException("A sample table cannot be written without samples.", nameof(plan));

        if ((sourceStream is null) != (sampleDescriptionBox is null))
            throw new ArgumentException("The source stream and original stsd box must be provided together.");

        _boxWriter.WriteBox(destination, "stbl", sampleTable =>
        {
            if (sourceStream is not null && sampleDescriptionBox is not null)
                _boxTreeWriter.Write(sourceStream, sampleTable, [sampleDescriptionBox]);

            WriteTimeToSample(sampleTable, plan.Samples);
            WriteSampleToChunk(sampleTable, plan.Samples);
            WriteSampleSizes(sampleTable, plan.Samples);
            WriteChunkOffsets(sampleTable, chunkOffsets, force64BitChunkOffsets);

            if (plan.HandlerType == Mp4HandlerType.Video)
                WriteSyncSamples(sampleTable, plan.Samples);

            if (plan.Samples.Any(sample => sample.CompositionOffsetTicks != 0))
                WriteCompositionTimes(sampleTable, plan.Samples);
        });
    }

    private void WriteTimeToSample(Stream destination, IReadOnlyList<Mp4SampleLocation> samples)
    {
        List<Mp4TimeToSampleEntry> entries = [];
        foreach (Mp4SampleLocation sample in samples)
        {
            if (entries.Count > 0 && entries[^1].SampleDelta == sample.DurationTicks)
                entries[^1] = entries[^1] with { SampleCount = checked(entries[^1].SampleCount + 1) };
            else
                entries.Add(new Mp4TimeToSampleEntry(1, sample.DurationTicks));
        }

        _boxWriter.WriteFullBox(destination, "stts", 0, 0, body =>
        {
            WriteUInt32(body, checked((uint)entries.Count));
            foreach (Mp4TimeToSampleEntry entry in entries)
            {
                WriteUInt32(body, entry.SampleCount);
                WriteUInt32(body, entry.SampleDelta);
            }
        });
    }

    private void WriteSampleToChunk(Stream destination, IReadOnlyList<Mp4SampleLocation> samples)
    {
        List<(uint FirstChunk, uint DescriptionIndex)> entries = [];
        for (int i = 0; i < samples.Count; i++)
        {
            uint description = samples[i].SampleDescriptionIndex;
            if (description == 0)
                throw new Mp4ParsingException("A planned sample has an invalid sample-description index.");

            if (i == 0 || samples[i - 1].SampleDescriptionIndex != description)
                entries.Add((checked((uint)i + 1), description));
        }

        _boxWriter.WriteFullBox(destination, "stsc", 0, 0, body =>
        {
            WriteUInt32(body, checked((uint)entries.Count));
            foreach ((uint firstChunk, uint descriptionIndex) in entries)
            {
                WriteUInt32(body, firstChunk);
                WriteUInt32(body, 1);
                WriteUInt32(body, descriptionIndex);
            }
        });
    }

    private void WriteSampleSizes(Stream destination, IReadOnlyList<Mp4SampleLocation> samples)
    {
        _boxWriter.WriteFullBox(destination, "stsz", 0, 0, body =>
        {
            WriteUInt32(body, 0);
            WriteUInt32(body, checked((uint)samples.Count));
            foreach (Mp4SampleLocation sample in samples)
                WriteUInt32(body, sample.Size);
        });
    }

    private void WriteChunkOffsets(
        Stream destination,
        IReadOnlyList<ulong> offsets,
        bool force64Bit)
    {
        bool use64Bit = force64Bit || offsets.Any(offset => offset > uint.MaxValue);
        string type = use64Bit ? "co64" : "stco";

        _boxWriter.WriteFullBox(destination, type, 0, 0, body =>
        {
            WriteUInt32(body, checked((uint)offsets.Count));
            foreach (ulong offset in offsets)
            {
                if (use64Bit)
                    WriteUInt64(body, offset);
                else
                    WriteUInt32(body, checked((uint)offset));
            }
        });
    }

    private void WriteSyncSamples(Stream destination, IReadOnlyList<Mp4SampleLocation> samples)
    {
        uint[] syncSamples = samples
            .Select((sample, index) => (sample, index))
            .Where(item => item.sample.IsSyncSample)
            .Select(item => checked((uint)item.index + 1))
            .ToArray();

        if (syncSamples.Length == 0)
            throw new Mp4ParsingException("A video trim must contain at least one sync sample.");

        _boxWriter.WriteFullBox(destination, "stss", 0, 0, body =>
        {
            WriteUInt32(body, checked((uint)syncSamples.Length));
            foreach (uint sampleNumber in syncSamples)
                WriteUInt32(body, sampleNumber);
        });
    }

    private void WriteCompositionTimes(Stream destination, IReadOnlyList<Mp4SampleLocation> samples)
    {
        bool signedOffsets = samples.Any(sample => sample.CompositionOffsetTicks < 0);
        List<Mp4CompositionTimeEntry> entries = [];

        foreach (Mp4SampleLocation sample in samples)
        {
            if (entries.Count > 0 && entries[^1].SampleOffset == sample.CompositionOffsetTicks)
                entries[^1] = entries[^1] with { SampleCount = checked(entries[^1].SampleCount + 1) };
            else
                entries.Add(new Mp4CompositionTimeEntry(1, sample.CompositionOffsetTicks));
        }

        _boxWriter.WriteFullBox(destination, "ctts", signedOffsets ? (byte)1 : (byte)0, 0, body =>
        {
            WriteUInt32(body, checked((uint)entries.Count));
            foreach (Mp4CompositionTimeEntry entry in entries)
            {
                WriteUInt32(body, entry.SampleCount);
                if (signedOffsets)
                    WriteInt32(body, entry.SampleOffset);
                else
                    WriteUInt32(body, checked((uint)entry.SampleOffset));
            }
        });
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

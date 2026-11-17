
using System.Buffers.Binary;
using System.Reflection;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4SampleTableReaderTests
{
    private readonly Mp4SampleTableReader _reader = new();

    [Fact]
    public void Read_ShouldReadCompositionTimeTable()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl,
            includeCompositionTimes: true);

        Mp4SampleTable result = _reader.Read(stream, stbl);

        Assert.NotNull(result.CompositionTimes);
        Assert.Equal(2U, result.CompositionTimes.EntryCount);

        Assert.Equal(
            new Mp4CompositionTimeEntry(2, 50),
            result.CompositionTimes.Entries[0]);

        Assert.Equal(
            new Mp4CompositionTimeEntry(3, -25),
            result.CompositionTimes.Entries[1]);
    }

    [Fact]
    public void Read_ShouldAllowMissingCompositionTimeBox()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl,
            includeCompositionTimes: false);

        Mp4SampleTable result = _reader.Read(stream, stbl);

        Assert.Null(result.CompositionTimes);
    }

    [Fact]
    public void Read_ShouldRejectDuplicateCompositionTimeBoxes()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl,
            includeCompositionTimes: true);

        AddChild(stbl, CreateBox("ctts"));

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stbl));
    }

    [Fact]
    public void Read_ShouldReadAllSampleTables()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl);

        Mp4SampleTable result = _reader.Read(stream, stbl);

        Assert.Equal(2, result.TimeToSampleEntries.Count);
        Assert.Equal(2, result.SampleToChunkEntries.Count);
        Assert.Equal(5U, result.SampleSizes.SampleCount);
        Assert.Equal(2U, result.ChunkOffsets.ChunkCount);
        Assert.NotNull(result.SyncSamples);

        Assert.Equal(
            new Mp4TimeToSampleEntry(2, 100),
            result.TimeToSampleEntries[0]);

        Assert.Equal(
            new Mp4SampleToChunkEntry(1, 2, 1),
            result.SampleToChunkEntries[0]);

        Assert.Equal(100U, result.SampleSizes.GetSampleSize(0));
        Assert.Equal(1000UL, result.ChunkOffsets.GetOffset(0));

        Assert.True(result.SyncSamples.IsSyncSample(1));
        Assert.True(result.SyncSamples.IsSyncSample(4));
    }

    [Fact]
    public void Read_ShouldSupportCo64Box()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl,
            useCo64: true);

        Mp4SampleTable result = _reader.Read(stream, stbl);

        Assert.Equal(2U, result.ChunkOffsets.ChunkCount);
        Assert.Equal(1000UL, result.ChunkOffsets.GetOffset(0));
        Assert.Equal(2000UL, result.ChunkOffsets.GetOffset(1));
    }

    [Fact]
    public void Read_ShouldAllowMissingSyncSampleBox()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl,
            includeSyncSamples: false);

        Mp4SampleTable result = _reader.Read(stream, stbl);

        Assert.Null(result.SyncSamples);
    }

    [Fact]
    public void Read_ShouldAllowEmptySyncSampleBox()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl,
            syncSampleNumbers: []);

        Mp4SampleTable result = _reader.Read(stream, stbl);

        Assert.NotNull(result.SyncSamples);
        Assert.Empty(result.SyncSamples.SampleNumbers);
    }

    [Fact]
    public void Read_ShouldRejectWrongBoxType()
    {
        Mp4Box box = CreateBox("moov");

        using MemoryStream stream = new();

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectNullStream()
    {
        Mp4Box box = CreateBox("stbl");

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(null!, box));
    }

    [Fact]
    public void Read_ShouldRejectNullBox()
    {
        using MemoryStream stream = new();

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(stream, null!));
    }

    [Fact]
    public void Read_ShouldRejectMissingRequiredBox()
    {
        using MemoryStream stream = new();

        Mp4Box stbl = CreateBox("stbl");

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stbl));
    }

    [Fact]
    public void Read_ShouldRejectDuplicateRequiredBox()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl);

        AddChild(stbl, CreateBox("stts"));

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stbl));
    }

    [Fact]
    public void Read_ShouldRejectMissingChunkOffsetBox()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl);

        Mp4Box offsetBox = stbl.Children
            .Single(child => child.Type == "stco");

        RemoveChild(stbl, offsetBox);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stbl));
    }

    [Fact]
    public void Read_ShouldRejectBothChunkOffsetBoxes()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl);

        AddChild(stbl, CreateBox("co64"));

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stbl));
    }

    [Fact]
    public void Read_ShouldRejectDuplicateSyncSampleBoxes()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl);

        AddChild(stbl, CreateBox("stss"));

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stbl));
    }

    [Fact]
    public void Read_ShouldRestoreOriginalStreamPosition()
    {
        using MemoryStream stream = CreateSampleStream(
            out Mp4Box stbl);

        stream.Position = 3;

        _reader.Read(stream, stbl);

        Assert.Equal(3, stream.Position);
    }


    private static byte[] CreateCompositionTimeData(
        params (uint SampleCount, uint SampleOffset)[] entries)
    {
        using MemoryStream stream = new();

        // Version 1: composition offsets are signed.
        stream.WriteByte(1);

        // Flags.
        stream.Write(new byte[3]);

        Span<byte> data = stackalloc byte[4];

        // Entry count.
        BinaryPrimitives.WriteUInt32BigEndian(
            data,
            checked((uint)entries.Length));

        stream.Write(data);

        foreach ((uint sampleCount, uint sampleOffset) in entries)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data, sampleCount);
            stream.Write(data);

            BinaryPrimitives.WriteUInt32BigEndian(data, sampleOffset);
            stream.Write(data);
        }

        return stream.ToArray();
    }


    private static MemoryStream CreateSampleStream(
        out Mp4Box stbl,
        bool useCo64 = false,
        bool includeSyncSamples = true,
        uint[]? syncSampleNumbers = null,
        bool includeCompositionTimes = false)
    {
        MemoryStream stream = new();

        stbl = CreateBox("stbl");

        AddBox(stbl, stream, "stts",
            CreateBoxData(2, 2, 100, 3, 200));

        if (includeCompositionTimes)
        {
            AddBox(stbl, stream, "ctts",
                CreateCompositionTimeData(
                    (2, 50),
                    (3, unchecked((uint)-25))));
        }

        AddBox(stbl, stream, "stsc",
            CreateBoxData(2, 1, 2, 1, 2, 3, 1));

        AddBox(stbl, stream, "stsz",
            CreateBoxData(0, 5, 100, 200, 300, 400, 500));

        if (useCo64)
        {
            AddBox(stbl, stream, "co64",
                CreateChunkOffsetData64(1000, 2000));
        }
        else
        {
            AddBox(stbl, stream, "stco",
                CreateBoxData(2, 1000, 2000));
        }

        if (includeSyncSamples)
        {
            uint[] samples = syncSampleNumbers ?? [1, 4];

            AddBox(stbl, stream, "stss",
                CreateSyncSampleData(samples));
        }

        stream.Position = 0;

        return stream;
    }

    private static byte[] CreateChunkOffsetData64(params ulong[] offsets)
    {
        using MemoryStream stream = new();

        // Version and flags.
        stream.Write(new byte[4]);

        Span<byte> data = stackalloc byte[8];

        // Entry count.
        BinaryPrimitives.WriteUInt32BigEndian(
            data[..4],
            checked((uint)offsets.Length));

        stream.Write(data[..4]);

        // 64-bit chunk offsets.
        foreach (ulong offset in offsets)
        {
            BinaryPrimitives.WriteUInt64BigEndian(data, offset);
            stream.Write(data);
        }

        return stream.ToArray();
    }

    private static byte[] CreateSyncSampleData(uint[] samples)
    {
        using MemoryStream stream = new();

        stream.Write(new byte[4]);

        Span<byte> data = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(
            data,
            checked((uint)samples.Length));

        stream.Write(data);

        foreach (uint sample in samples)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data, sample);
            stream.Write(data);
        }

        return stream.ToArray();
    }

    private static void AddBox(
        Mp4Box parent,
        MemoryStream stream,
        string type,
        byte[] data)
    {
        long offset = stream.Position;

        // Reserve the 8-byte MP4 box header.
        stream.Write(new byte[8]);

        // The payload starts immediately after the header.
        stream.Write(data);

        Mp4Box box = new(
            size: checked((ulong)(data.Length + 8)),
            type: type,
            offset: offset,
            headerSize: 8,
            usesExtendedSize: false,
            extendsToEnd: false);

        AddChild(parent, box);
    }

    private static byte[] CreateBoxData(params uint[] values)
    {
        using MemoryStream stream = new();

        stream.Write(new byte[4]);

        Span<byte> data = stackalloc byte[4];

        foreach (uint value in values)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data, value);
            stream.Write(data);
        }

        return stream.ToArray();
    }

    private static byte[] CreateBoxData64(params ulong[] values)
    {
        using MemoryStream stream = new();

        stream.Write(new byte[4]);

        Span<byte> data = stackalloc byte[8];

        foreach (ulong value in values)
        {
            BinaryPrimitives.WriteUInt64BigEndian(data, value);
            stream.Write(data);
        }

        return stream.ToArray();
    }

    private static Mp4Box CreateBox(string type)
    {
        return new Mp4Box(
            size: 8,
            type: type,
            offset: 0,
            headerSize: 8,
            usesExtendedSize: false,
            extendsToEnd: false);
    }

    private static void AddChild(Mp4Box parent, Mp4Box child)
    {
        MethodInfo method = typeof(Mp4Box).GetMethod(
            "AddChild",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Could not find Mp4Box.AddChild.");

        method.Invoke(parent, [child]);
    }

    private static void RemoveChild(Mp4Box parent, Mp4Box child)
    {
        PropertyInfo property = typeof(Mp4Box).GetProperty(
            "Children",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException(
                "Could not find Mp4Box.Children.");

        object? children = property.GetValue(parent);

        MethodInfo? removeMethod = children?.GetType().GetMethod(
            "Remove",
            [typeof(Mp4Box)]);

        removeMethod?.Invoke(children, [child]);
    }
}

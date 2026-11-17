
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4CompositionTimeReaderTests
{
    private readonly Mp4CompositionTimeReader _reader = new();

    [Fact]
    public void Read_ShouldParseSingleEntry()
    {
        byte[] data = CreateCttsBox(0, (30, 1000));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4CompositionTimeEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Mp4CompositionTimeEntry entry = Assert.Single(entries);

        Assert.Equal(30u, entry.SampleCount);
        Assert.Equal(1000, entry.SampleOffset);
    }

    [Fact]
    public void Read_ShouldParseMultipleEntries()
    {
        byte[] data = CreateCttsBox(
            0,
            (20, 1000),
            (10, 500));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4CompositionTimeEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(2, entries.Count);

        Assert.Equal(20u, entries[0].SampleCount);
        Assert.Equal(1000, entries[0].SampleOffset);

        Assert.Equal(10u, entries[1].SampleCount);
        Assert.Equal(500, entries[1].SampleOffset);
    }

    [Fact]
    public void Read_ShouldAllowEmptyTable()
    {
        byte[] data = CreateCttsBox(0);

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4CompositionTimeEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Assert.Empty(entries);
    }

    [Fact]
    public void Read_ShouldParseVersionOneNegativeOffset()
    {
        byte[] data = CreateCttsBox(1, (5, -100));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4CompositionTimeEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Mp4CompositionTimeEntry entry = Assert.Single(entries);

        Assert.Equal(5u, entry.SampleCount);
        Assert.Equal(-100, entry.SampleOffset);
    }

    [Fact]
    public void Read_ShouldParseVersionOnePositiveOffset()
    {
        byte[] data = CreateCttsBox(1, (5, 100));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4CompositionTimeEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Mp4CompositionTimeEntry entry = Assert.Single(entries);

        Assert.Equal(5u, entry.SampleCount);
        Assert.Equal(100, entry.SampleOffset);
    }

    [Fact]
    public void Read_ShouldParseVersionOneMinimumNegativeOffset()
    {
        byte[] data = CreateCttsBox(1, (1, int.MinValue));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4CompositionTimeEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Mp4CompositionTimeEntry entry = Assert.Single(entries);

        Assert.Equal(int.MinValue, entry.SampleOffset);
    }

    [Fact]
    public void Read_ShouldParseVersionZeroMaximumSupportedOffset()
    {
        byte[] data = CreateCttsBox(0, (1, int.MaxValue));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4CompositionTimeEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Mp4CompositionTimeEntry entry = Assert.Single(entries);

        Assert.Equal(int.MaxValue, entry.SampleOffset);
    }

    [Fact]
    public void Read_ShouldRejectVersionZeroOffsetAboveIntMaxValue()
    {
        byte[] data = CreateCttsBoxRaw(0, (1, 0x80000000));

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateCttsBox(2, (10, 1000));

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateCttsBox(0, (10, 1000));

        using MemoryStream stream = new(data);

        Mp4Box box = new(
            (ulong)data.Length,
            "free",
            0,
            8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectMismatchedEntryCount()
    {
        byte[] data = CreateCttsBox(0, (10, 1000));

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            2);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectZeroSampleCount()
    {
        byte[] data = CreateCttsBox(0, (0, 1000));

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateCttsBox(0, (30, 1000));

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data));

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "ctts",
            0,
            8);
    }

    private static byte[] CreateCttsBox(
        byte version,
        params (uint Count, int Offset)[] entries)
    {
        (uint Count, uint Offset)[] rawEntries = entries
            .Select(entry => (
                entry.Count,
                unchecked((uint)entry.Offset)))
            .ToArray();

        return CreateCttsBoxRaw(version, rawEntries);
    }

    private static byte[] CreateCttsBoxRaw(
        byte version,
        params (uint Count, uint Offset)[] entries)
    {
        byte[] data = new byte[16 + entries.Length * 8];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes("ctts")
            .CopyTo(data, 4);

        // FullBox version and flags.
        data[8] = version;

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            (uint)entries.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            int offset = 16 + i * 8;

            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(offset, 4),
                entries[i].Count);

            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(offset + 4, 4),
                entries[i].Offset);
        }

        return data;
    }
}

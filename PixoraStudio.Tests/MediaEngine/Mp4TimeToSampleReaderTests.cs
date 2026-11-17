
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4TimeToSampleReaderTests
{
    private readonly Mp4TimeToSampleReader _reader = new();

    [Fact]
    public void Read_ShouldParseSingleEntry()
    {
        byte[] data = CreateSttsBox(
            (30, 1000));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4TimeToSampleEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Mp4TimeToSampleEntry entry = Assert.Single(entries);

        Assert.Equal(30u, entry.SampleCount);
        Assert.Equal(1000u, entry.SampleDelta);
    }

    [Fact]
    public void Read_ShouldParseMultipleEntries()
    {
        byte[] data = CreateSttsBox(
            (20, 1000),
            (10, 500));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4TimeToSampleEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(2, entries.Count);

        Assert.Equal(20u, entries[0].SampleCount);
        Assert.Equal(1000u, entries[0].SampleDelta);

        Assert.Equal(10u, entries[1].SampleCount);
        Assert.Equal(500u, entries[1].SampleDelta);
    }

    [Fact]
    public void Read_ShouldAllowEmptyTable()
    {
        byte[] data = CreateSttsBox();

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4TimeToSampleEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Assert.Empty(entries);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateSttsBox((10, 1000));

        data[8] = 1;

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateSttsBox((10, 1000));

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
        byte[] data = CreateSttsBox((10, 1000));

        // Change entry_count from 1 to 2.
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
        byte[] data = CreateSttsBox((0, 1000));

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateSttsBox((30, 1000));

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data));

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "stts",
            0,
            8);
    }

    private static byte[] CreateSttsBox(
        params (uint Count, uint Delta)[] entries)
    {
        byte[] data = new byte[16 + entries.Length * 8];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes("stts")
            .CopyTo(data, 4);

        // FullBox version and flags.
        data[8] = 0;

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
                entries[i].Delta);
        }

        return data;
    }
}

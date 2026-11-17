
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4SampleToChunkReaderTests
{
    private readonly Mp4SampleToChunkReader _reader = new();

    [Fact]
    public void Read_ShouldParseSingleEntry()
    {
        byte[] data = CreateStscBox(
            (1, 4, 1));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4SampleToChunkEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Mp4SampleToChunkEntry entry = Assert.Single(entries);

        Assert.Equal(1u, entry.FirstChunk);
        Assert.Equal(4u, entry.SamplesPerChunk);
        Assert.Equal(1u, entry.SampleDescriptionIndex);
    }

    [Fact]
    public void Read_ShouldParseMultipleEntries()
    {
        byte[] data = CreateStscBox(
            (1, 4, 1),
            (5, 2, 1),
            (10, 1, 1));

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4SampleToChunkEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Assert.Equal(3, entries.Count);

        Assert.Equal(1u, entries[0].FirstChunk);
        Assert.Equal(4u, entries[0].SamplesPerChunk);

        Assert.Equal(5u, entries[1].FirstChunk);
        Assert.Equal(2u, entries[1].SamplesPerChunk);

        Assert.Equal(10u, entries[2].FirstChunk);
        Assert.Equal(1u, entries[2].SamplesPerChunk);
    }

    [Fact]
    public void Read_ShouldAllowEmptyTable()
    {
        byte[] data = CreateStscBox();

        using MemoryStream stream = new(data);

        IReadOnlyList<Mp4SampleToChunkEntry> entries =
            _reader.Read(stream, CreateBox(data));

        Assert.Empty(entries);
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateStscBox((1, 4, 1));

        data[8] = 1;

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectMismatchedEntryCount()
    {
        byte[] data = CreateStscBox((1, 4, 1));

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            2);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 4, 0)]
    public void Read_ShouldRejectInvalidEntryValues(
        uint firstChunk,
        uint samplesPerChunk,
        uint descriptionIndex)
    {
        byte[] data = CreateStscBox(
            (firstChunk, samplesPerChunk, descriptionIndex));

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectUnorderedEntries()
    {
        byte[] data = CreateStscBox(
            (1, 4, 1),
            (1, 2, 1));

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldRejectFirstEntryNotStartingAtOne()
    {
        byte[] data = CreateStscBox(
            (2, 4, 1));

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data)));
    }

    [Fact]
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateStscBox((1, 4, 1));

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data));

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(byte[] data)
    {
        return new Mp4Box(
            (ulong)data.Length,
            "stsc",
            0,
            8);
    }

    private static byte[] CreateStscBox(
        params (uint FirstChunk, uint SamplesPerChunk,
            uint DescriptionIndex)[] entries)
    {
        byte[] data = new byte[16 + entries.Length * 12];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes("stsc")
            .CopyTo(data, 4);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            (uint)entries.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            int offset = 16 + i * 12;

            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(offset, 4),
                entries[i].FirstChunk);

            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(offset + 4, 4),
                entries[i].SamplesPerChunk);

            BinaryPrimitives.WriteUInt32BigEndian(
                data.AsSpan(offset + 8, 4),
                entries[i].DescriptionIndex);
        }

        return data;
    }
}

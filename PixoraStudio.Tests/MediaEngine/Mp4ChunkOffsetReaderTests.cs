using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4ChunkOffsetReaderTests
{
    private readonly Mp4ChunkOffsetReader _reader = new();

    [Fact]
    public void Read_ShouldParse32BitChunkOffsets()
    {
        byte[] data = CreateChunkOffsetBox(
            "stco",
            100,
            500,
            1200);

        using MemoryStream stream = new(data);

        Mp4ChunkOffsetTable table =
            _reader.Read(stream, CreateBox(data, "stco"));

        Assert.Equal(3u, table.ChunkCount);

        Assert.Equal(100UL, table.GetOffset(0));
        Assert.Equal(500UL, table.GetOffset(1));
        Assert.Equal(1200UL, table.GetOffset(2));
    }

    [Fact]
    public void Read_ShouldParse64BitChunkOffsets()
    {
        byte[] data = CreateChunkOffsetBox(
            "co64",
            100UL,
            5_000_000_000UL,
            9_000_000_000UL);

        using MemoryStream stream = new(data);

        Mp4ChunkOffsetTable table =
            _reader.Read(stream, CreateBox(data, "co64"));

        Assert.Equal(3u, table.ChunkCount);

        Assert.Equal(100UL, table.GetOffset(0));
        Assert.Equal(5_000_000_000UL, table.GetOffset(1));
        Assert.Equal(9_000_000_000UL, table.GetOffset(2));
    }

    [Fact]
    public void Read_ShouldAllowEmptyTable()
    {
        byte[] data = CreateChunkOffsetBox("stco");

        using MemoryStream stream = new(data);

        Mp4ChunkOffsetTable table =
            _reader.Read(stream, CreateBox(data, "stco"));

        Assert.Equal(0u, table.ChunkCount);
        Assert.Empty(table.Offsets);
    }

    [Fact]
    public void Read_ShouldRejectOutOfRangeChunkIndex()
    {
        byte[] data = CreateChunkOffsetBox(
            "stco",
            100);

        using MemoryStream stream = new(data);

        Mp4ChunkOffsetTable table =
            _reader.Read(stream, CreateBox(data, "stco"));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.GetOffset(1));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        byte[] data = CreateChunkOffsetBox(
            "stco",
            100);

        data[8] = 1;

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data, "stco")));
    }

    [Fact]
    public void Read_ShouldRejectMismatchedChunkCount()
    {
        byte[] data = CreateChunkOffsetBox(
            "stco",
            100,
            200);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            3);

        using MemoryStream stream = new(data);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, CreateBox(data, "stco")));
    }

    [Fact]
    public void Read_ShouldRejectIncorrectBoxType()
    {
        byte[] data = CreateChunkOffsetBox(
            "stco",
            100);

        using MemoryStream stream = new(data);

        Mp4Box box = CreateBox(data, "free");

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectBoxTooSmall()
    {
        byte[] data = new byte[15];

        using MemoryStream stream = new(data);

        Mp4Box box = new(
            (ulong)data.Length,
            "stco",
            0,
            8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldPreserveStreamPosition()
    {
        byte[] data = CreateChunkOffsetBox(
            "stco",
            100,
            200);

        using MemoryStream stream = new(data);

        stream.Position = 4;

        _reader.Read(stream, CreateBox(data, "stco"));

        Assert.Equal(4, stream.Position);
    }

    private static Mp4Box CreateBox(
        byte[] data,
        string type)
    {
        return new Mp4Box(
            (ulong)data.Length,
            type,
            0,
            8);
    }

    private static byte[] CreateChunkOffsetBox(
        string type,
        params ulong[] offsets)
    {
        int offsetSize = type == "stco" ? 4 : 8;

        byte[] data = new byte[16 + offsets.Length * offsetSize];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes(type)
            .CopyTo(data, 4);

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(12, 4),
            (uint)offsets.Length);

        for (int i = 0; i < offsets.Length; i++)
        {
            if (offsetSize == 4)
            {
                if (offsets[i] > uint.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(offsets));
                }

                BinaryPrimitives.WriteUInt32BigEndian(
                    data.AsSpan(16 + i * 4, 4),
                    (uint)offsets[i]);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(
                    data.AsSpan(16 + i * 8, 8),
                    offsets[i]);
            }
        }

        return data;
    }
}
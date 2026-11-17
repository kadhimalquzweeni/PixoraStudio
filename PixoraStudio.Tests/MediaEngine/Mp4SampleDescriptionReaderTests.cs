
using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4SampleDescriptionReaderTests
{
    private readonly Mp4SampleDescriptionReader _reader = new();

    [Fact]
    public void Read_ShouldReturnSingleSampleEntry()
    {
        using MemoryStream stream = CreateStsd(
            ("avc1", new byte[] { 1, 2, 3 }));

        Mp4Box stsd = CreateStsdBox(stream);

        IReadOnlyList<Mp4Box> entries = _reader.Read(stream, stsd);

        Mp4Box entry = Assert.Single(entries);

        Assert.Equal("avc1", entry.Type);
        Assert.Equal(11UL, entry.Size);
        Assert.Equal(new byte[] { 1, 2, 3 },
            ReadBoxData(stream, entry));
    }

    [Fact]
    public void Read_ShouldReturnMultipleSampleEntries()
    {
        using MemoryStream stream = CreateStsd(
            ("avc1", new byte[] { 1 }),
            ("hvc1", new byte[] { 2, 3 }));

        Mp4Box stsd = CreateStsdBox(stream);

        IReadOnlyList<Mp4Box> entries = _reader.Read(stream, stsd);

        Assert.Equal(2, entries.Count);
        Assert.Equal("avc1", entries[0].Type);
        Assert.Equal("hvc1", entries[1].Type);
    }

    [Fact]
    public void Read_ShouldRestoreStreamPosition()
    {
        using MemoryStream stream = CreateStsd(
            ("avc1", new byte[] { 1 }));

        stream.Position = 3;
        Mp4Box stsd = CreateStsdBox(stream);

        _reader.Read(stream, stsd);

        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void Read_ShouldRejectWrongBoxType()
    {
        using MemoryStream stream = new();

        Mp4Box box = new(
            8, "moov", 0, 8);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, box));
    }

    [Fact]
    public void Read_ShouldRejectUnsupportedVersion()
    {
        using MemoryStream stream = CreateStsd(
            ("avc1", []));

        stream.Position = 8;
        stream.WriteByte(1);

        Mp4Box stsd = CreateStsdBox(stream);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stsd));
    }

    [Fact]
    public void Read_ShouldRejectEntryCountMismatch()
    {
        using MemoryStream stream = CreateStsd(
            ("avc1", []));

        // Change the declared count from one to two.
        stream.Position = 12;

        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(count, 2);
        stream.Write(count);

        Mp4Box stsd = CreateStsdBox(stream);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stsd));
    }

    [Fact]
    public void Read_ShouldRejectInvalidEntrySize()
    {
        using MemoryStream stream = CreateStsd(
            ("avc1", []));

        // The entry starts at offset 16.
        stream.Position = 16;

        Span<byte> invalidSize = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(
            invalidSize, 4);

        stream.Write(invalidSize);

        Mp4Box stsd = CreateStsdBox(stream);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, stsd));
    }

    private static MemoryStream CreateStsd(
        params (string Type, byte[] Data)[] entries)
    {
        MemoryStream stream = new();

        // stsd box header.
        stream.Write(new byte[8]);

        // FullBox version and flags.
        stream.Write(new byte[4]);

        WriteUInt32(stream, checked((uint)entries.Length));

        foreach (var entry in entries)
        {
            WriteUInt32(
                stream,
                checked((uint)(8 + entry.Data.Length)));

            stream.Write(
                System.Text.Encoding.ASCII.GetBytes(entry.Type));

            stream.Write(entry.Data);
        }

        long size = stream.Length;

        stream.Position = 0;

        WriteUInt32(stream, checked((uint)size));
        stream.Write(
            System.Text.Encoding.ASCII.GetBytes("stsd"));

        stream.Position = 0;

        return stream;
    }

    private static Mp4Box CreateStsdBox(
        MemoryStream stream)
    {
        return new Mp4Box(
            checked((ulong)stream.Length),
            "stsd",
            0,
            8);
    }

    private static byte[] ReadBoxData(
        MemoryStream stream,
        Mp4Box box)
    {
        byte[] data = new byte[checked((int)box.DataSize)];

        stream.Position = box.DataOffset;
        stream.ReadExactly(data);

        return data;
    }

    private static void WriteUInt32(
        Stream stream,
        uint value)
    {
        Span<byte> buffer = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(
            buffer, value);

        stream.Write(buffer);
    }
}

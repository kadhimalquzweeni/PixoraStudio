
using System.Buffers.Binary;
using System.Reflection;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4TrackSampleTableReaderTests
{
    private readonly Mp4TrackSampleTableReader _reader = new();

    [Fact]
    public void Read_ShouldReadSampleTablesFromTrack()
    {
        Mp4Box track = CreateValidTrack(out MemoryStream stream);

        using (stream)
        {
            Mp4SampleTable result = _reader.Read(stream, track);

            Assert.Single(result.TimeToSampleEntries);
            Assert.Single(result.SampleToChunkEntries);
            Assert.Equal(1U, result.SampleSizes.SampleCount);
            Assert.Equal(1U, result.ChunkOffsets.ChunkCount);

            Assert.Equal(
                new Mp4TimeToSampleEntry(1, 100),
                result.TimeToSampleEntries[0]);

            Assert.Equal(50U, result.SampleSizes.GetSampleSize(0));
            Assert.Equal(1000UL, result.ChunkOffsets.GetOffset(0));

            // No stss box means all samples are treated as sync samples.
            Assert.Null(result.SyncSamples);
        }
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
    public void Read_ShouldRejectMissingMediaBox()
    {
        Mp4Box track = CreateBox("trak");

        using MemoryStream stream = new();

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, track));
    }

    [Fact]
    public void Read_ShouldRejectMissingMediaInformationBox()
    {
        Mp4Box track = CreateBox("trak");
        Mp4Box mdia = CreateBox("mdia");

        AddChild(track, mdia);

        using MemoryStream stream = new();

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, track));
    }

    [Fact]
    public void Read_ShouldRejectMissingSampleTableBox()
    {
        Mp4Box track = CreateBox("trak");
        Mp4Box mdia = CreateBox("mdia");
        Mp4Box minf = CreateBox("minf");

        AddChild(track, mdia);
        AddChild(mdia, minf);

        using MemoryStream stream = new();

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, track));
    }

    [Fact]
    public void Read_ShouldRejectNullStream()
    {
        Mp4Box track = CreateBox("trak");

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(null!, track));
    }

    [Fact]
    public void Read_ShouldRejectNullTrack()
    {
        using MemoryStream stream = new();

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(stream, null!));
    }

    private static Mp4Box CreateValidTrack(out MemoryStream stream)
    {
        stream = new MemoryStream();

        Mp4Box track = CreateBox("trak");
        Mp4Box mdia = CreateBox("mdia");
        Mp4Box minf = CreateBox("minf");
        Mp4Box stbl = CreateBox("stbl");

        AddChild(track, mdia);
        AddChild(mdia, minf);
        AddChild(minf, stbl);

        AddBox(stbl, stream, "stts",
            CreateBoxData(1, 1, 100));

        AddBox(stbl, stream, "stsc",
            CreateBoxData(1, 1, 1, 1));

        AddBox(stbl, stream, "stsz",
            CreateBoxData(0, 1, 50));

        AddBox(stbl, stream, "stco",
            CreateBoxData(1, 1000));

        stream.Position = 0;

        return track;
    }

    private static void AddBox(
        Mp4Box parent,
        MemoryStream stream,
        string type,
        byte[] data)
    {
        long offset = stream.Position;

        // Reserve the eight-byte box header.
        stream.Write(new byte[8]);

        // Write the box payload.
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

        // Version and flags.
        stream.Write(new byte[4]);

        Span<byte> data = stackalloc byte[4];

        foreach (uint value in values)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data, value);
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
}

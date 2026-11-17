
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4ReaderTests
{
    private readonly Mp4Reader _reader = new();

    [Fact]
    public void ReadTopLevelBoxes_ShouldReadNormalBox()
    {
        // Arrange
        byte[] data = CreateBox("ftyp");

        using MemoryStream stream = new(data);

        // Act
        IReadOnlyList<Mp4Box> boxes =
            _reader.ReadTopLevelBoxes(stream);

        // Assert
        Assert.Single(boxes);
        Assert.Equal("ftyp", boxes[0].Type);
        Assert.Equal((ulong)8, boxes[0].Size);
        Assert.Equal(0, boxes[0].Offset);
        Assert.Equal(8, boxes[0].HeaderSize);
        Assert.Equal(0, boxes[0].DataSize);
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldReadMultipleBoxes()
    {
        // Arrange
        byte[] first = CreateBox("ftyp");
        byte[] second = CreateBox("moov");
        byte[] third = CreateBox("mdat");

        byte[] data = first
            .Concat(second)
            .Concat(third)
            .ToArray();

        using MemoryStream stream = new(data);

        // Act
        IReadOnlyList<Mp4Box> boxes =
            _reader.ReadTopLevelBoxes(stream);

        // Assert
        Assert.Equal(3, boxes.Count);

        Assert.Equal("ftyp", boxes[0].Type);
        Assert.Equal("moov", boxes[1].Type);
        Assert.Equal("mdat", boxes[2].Type);

        Assert.Equal(0, boxes[0].Offset);
        Assert.Equal(8, boxes[1].Offset);
        Assert.Equal(16, boxes[2].Offset);
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldReadExtendedSizeBox()
    {
        // Arrange
        byte[] data = CreateExtendedBox("mdat");

        using MemoryStream stream = new(data);

        // Act
        IReadOnlyList<Mp4Box> boxes =
            _reader.ReadTopLevelBoxes(stream);

        // Assert
        Assert.Single(boxes);

        Assert.Equal("mdat", boxes[0].Type);
        Assert.Equal((ulong)16, boxes[0].Size);
        Assert.Equal(16, boxes[0].HeaderSize);
        Assert.True(boxes[0].UsesExtendedSize);
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldReadSizeZeroBox()
    {
        // Arrange
        byte[] data = CreateZeroSizeBox("mdat");

        using MemoryStream stream = new(data);

        // Act
        IReadOnlyList<Mp4Box> boxes =
            _reader.ReadTopLevelBoxes(stream);

        // Assert
        Assert.Single(boxes);

        Assert.Equal("mdat", boxes[0].Type);
        Assert.Equal((ulong)data.Length, boxes[0].Size);
        Assert.True(boxes[0].ExtendsToEnd);
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldReadUuidBox()
    {
        // Arrange
        byte[] data = CreateUuidBox();

        using MemoryStream stream = new(data);

        // Act
        IReadOnlyList<Mp4Box> boxes =
            _reader.ReadTopLevelBoxes(stream);

        // Assert
        Assert.Single(boxes);

        Assert.Equal("uuid", boxes[0].Type);
        Assert.Equal((ulong)24, boxes[0].Size);
        Assert.Equal(24, boxes[0].HeaderSize);
        Assert.Equal(0, boxes[0].DataSize);
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldRejectInvalidBoxSize()
    {
        // Arrange
        byte[] data = new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            7);

        Encoding.ASCII.GetBytes("ftyp")
            .CopyTo(data, 4);

        using MemoryStream stream = new(data);

        // Act & Assert
        Assert.Throws<Mp4ParsingException>(
            () => _reader.ReadTopLevelBoxes(stream));
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldRejectBoxOutsideFileBoundary()
    {
        // Arrange
        byte[] data = CreateBoxHeader(
            100,
            "mdat");

        using MemoryStream stream = new(data);

        // Act & Assert
        Assert.Throws<Mp4ParsingException>(
            () => _reader.ReadTopLevelBoxes(stream));
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldRejectIncompleteHeader()
    {
        // Arrange
        byte[] data = [0, 0, 0, 8];

        using MemoryStream stream = new(data);

        // Act & Assert
        Assert.Throws<Mp4ParsingException>(
            () => _reader.ReadTopLevelBoxes(stream));
    }

    [Fact]
    public void ReadTopLevelBoxes_ShouldRestoreOriginalStreamPosition()
    {
        // Arrange
        byte[] data = CreateBox("ftyp")
            .Concat(CreateBox("moov"))
            .ToArray();

        using MemoryStream stream = new(data);

        stream.Position = 4;

        // Act
        _reader.ReadTopLevelBoxes(stream);

        // Assert
        Assert.Equal(4, stream.Position);
    }

    // ------------------------------------
    // Test data builders
    // ------------------------------------


    private static byte[] CreateBox(
        string type,
        byte[]? payload = null)
    {
        payload ??= [];

        uint size = checked((uint)(8 + payload.Length));

        byte[] data = new byte[size];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            size);

        Encoding.ASCII.GetBytes(type)
            .CopyTo(data, 4);

        payload.CopyTo(data, 8);

        return data;
    }


    private static byte[] CreateExtendedBox(string type)
    {
        byte[] data = new byte[16];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            1);

        Encoding.ASCII.GetBytes(type)
            .CopyTo(data, 4);

        BinaryPrimitives.WriteUInt64BigEndian(
            data.AsSpan(8, 8),
            16);

        return data;
    }

    private static byte[] CreateZeroSizeBox(string type)
    {
        byte[] data = new byte[12];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            0);

        Encoding.ASCII.GetBytes(type)
            .CopyTo(data, 4);

        data[8] = 1;
        data[9] = 2;
        data[10] = 3;
        data[11] = 4;

        return data;
    }

    private static byte[] CreateUuidBox()
    {
        byte[] data = new byte[24];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            24);

        Encoding.ASCII.GetBytes("uuid")
            .CopyTo(data, 4);

        // The remaining 16 bytes represent the UUID.
        return data;
    }

    private static byte[] CreateBoxHeader(
        uint size,
        string type)
    {
        byte[] data = new byte[8];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            size);

        Encoding.ASCII.GetBytes(type)
            .CopyTo(data, 4);

        return data;
    }

    [Fact]
    public void ReadBoxTree_ShouldParseNestedBoxes()
    {
        // Arrange
        byte[] stbl = CreateBox("stbl");
        byte[] minf = CreateBox("minf", stbl);
        byte[] mdia = CreateBox("mdia", minf);
        byte[] trak = CreateBox("trak", mdia);
        byte[] moov = CreateBox("moov", trak);

        using MemoryStream stream = new(moov);

        // Act
        IReadOnlyList<Mp4Box> boxes =
            _reader.ReadBoxTree(stream);

        // Assert
        Assert.Single(boxes);

        Mp4Box moovBox = boxes[0];

        Assert.Equal("moov", moovBox.Type);
        Assert.Single(moovBox.Children);

        Mp4Box trakBox = moovBox.Children[0];

        Assert.Equal("trak", trakBox.Type);
        Assert.Single(trakBox.Children);

        Mp4Box mdiaBox = trakBox.Children[0];

        Assert.Equal("mdia", mdiaBox.Type);
        Assert.Single(mdiaBox.Children);

        Mp4Box minfBox = mdiaBox.Children[0];

        Assert.Equal("minf", minfBox.Type);
        Assert.Single(minfBox.Children);

        Assert.Equal("stbl", minfBox.Children[0].Type);
    }

    [Fact]
    public void ReadBoxTree_ShouldPreserveUnknownBoxes()
    {
        // Arrange
        byte[] unknown = CreateBox("abcd");

        byte[] moov = CreateBox("moov", unknown);

        using MemoryStream stream = new(moov);

        // Act
        IReadOnlyList<Mp4Box> boxes =
            _reader.ReadBoxTree(stream);

        // Assert
        Assert.Single(boxes);
        Assert.Single(boxes[0].Children);

        Assert.Equal(
            "abcd",
            boxes[0].Children[0].Type);

        Assert.Empty(boxes[0].Children[0].Children);
    }

    [Fact]
    public void ReadBoxTree_ShouldPreserveStreamPosition()
    {
        // Arrange
        byte[] data = CreateBox(
            "moov",
            CreateBox("trak"));

        using MemoryStream stream = new(data);

        stream.Position = 4;

        // Act
        _reader.ReadBoxTree(stream);

        // Assert
        Assert.Equal(4, stream.Position);
    }

    [Fact]
    public void ReadBoxTree_ShouldRejectIncompleteNestedHeader()
    {
        // Arrange
        byte[] incompleteHeader = [0, 0, 0, 8];

        byte[] moov = CreateBox(
            "moov",
            incompleteHeader);

        using MemoryStream stream = new(moov);

        // Act & Assert
        Assert.Throws<Mp4ParsingException>(
            () => _reader.ReadBoxTree(stream));
    }

}

using System.Reflection;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4MediaDataReaderTests
{
    private readonly Mp4MediaDataReader _reader = new();

    [Fact]
    public void Read_ShouldFindMediaDataBox()
    {
        Mp4Box root = CreateBox("root");

        AddChild(root, CreateBox(
            "mdat",
            size: 108,
            offset: 100,
            headerSize: 8));

        IReadOnlyList<Mp4MediaData> result =
            _reader.Read(root);

        Assert.Single(result);
        Assert.Equal(108, result[0].DataOffset);
        Assert.Equal(100, result[0].DataSize);
    }

    [Fact]
    public void Read_ShouldFindMultipleMediaDataBoxes()
    {
        Mp4Box root = CreateBox("root");

        AddChild(root, CreateBox(
            "mdat",
            size: 108,
            offset: 100));

        AddChild(root, CreateBox(
            "mdat",
            size: 208,
            offset: 200));

        IReadOnlyList<Mp4MediaData> result =
            _reader.Read(root);

        Assert.Equal(2, result.Count);

        Assert.Equal(108, result[0].DataOffset);
        Assert.Equal(100, result[0].DataSize);

        Assert.Equal(208, result[1].DataOffset);
        Assert.Equal(200, result[1].DataSize);
    }

    [Fact]
    public void Read_ShouldFindNestedMediaDataBox()
    {
        Mp4Box root = CreateBox("root");
        Mp4Box container = CreateBox("moov");

        AddChild(container, CreateBox(
            "mdat",
            size: 108,
            offset: 100));

        AddChild(root, container);

        IReadOnlyList<Mp4MediaData> result =
            _reader.Read(root);

        Assert.Single(result);
        Assert.Equal(108, result[0].DataOffset);
    }

    [Fact]
    public void Read_ShouldRejectMissingMediaDataBox()
    {
        Mp4Box root = CreateBox("root");

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(root));
    }

    [Fact]
    public void Read_ShouldRejectNullRoot()
    {
        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(null!));
    }

    [Fact]
    public void Read_ShouldReturnReadOnlyCollection()
    {
        Mp4Box root = CreateBox("root");

        AddChild(root, CreateBox(
            "mdat",
            size: 108,
            offset: 100));

        IReadOnlyList<Mp4MediaData> result =
            _reader.Read(root);

        Assert.IsAssignableFrom<IReadOnlyList<Mp4MediaData>>(
            result);

        Assert.Throws<NotSupportedException>(
            () => ((IList<Mp4MediaData>)result).Add(
                new Mp4MediaData(500, 100)));
    }

    private static Mp4Box CreateBox(
        string type,
        ulong size = 8,
        long offset = 0,
        long headerSize = 8)
    {
        return new Mp4Box(
            size,
            type,
            offset,
            headerSize);
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

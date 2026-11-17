
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using System.Reflection;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4SampleTableLocatorTests
{
    private readonly Mp4SampleTableLocator _locator = new();

    [Fact]
    public void Locate_ShouldReturnSampleTableBox()
    {
        Mp4Box trak = CreateTrackTree();

        Mp4Box result = _locator.Locate(trak);

        Assert.Equal("stbl", result.Type);
    }

    [Fact]
    public void Locate_ShouldRejectNullTrack()
    {
        Assert.Throws<ArgumentNullException>(
            () => _locator.Locate(null!));
    }

    [Fact]
    public void Locate_ShouldRejectNonTrackBox()
    {
        Mp4Box box = CreateBox("moov");

        Assert.Throws<Mp4ParsingException>(
            () => _locator.Locate(box));
    }

    [Fact]
    public void Locate_ShouldRejectMissingMediaBox()
    {
        Mp4Box trak = CreateBox("trak");

        Assert.Throws<Mp4ParsingException>(
            () => _locator.Locate(trak));
    }

    [Fact]
    public void Locate_ShouldRejectMissingMediaInformationBox()
    {
        Mp4Box trak = CreateBox("trak");
        AddChild(trak, CreateBox("mdia"));

        Assert.Throws<Mp4ParsingException>(
            () => _locator.Locate(trak));
    }

    [Fact]
    public void Locate_ShouldRejectMissingSampleTableBox()
    {
        Mp4Box trak = CreateBox("trak");
        Mp4Box mdia = CreateBox("mdia");
        Mp4Box minf = CreateBox("minf");

        AddChild(trak, mdia);
        AddChild(mdia, minf);

        Assert.Throws<Mp4ParsingException>(
            () => _locator.Locate(trak));
    }

    [Fact]
    public void Locate_ShouldRejectDuplicateMediaBoxes()
    {
        Mp4Box trak = CreateBox("trak");

        AddChild(trak, CreateBox("mdia"));
        AddChild(trak, CreateBox("mdia"));

        Assert.Throws<Mp4ParsingException>(
            () => _locator.Locate(trak));
    }

    [Fact]
    public void Locate_ShouldRejectDuplicateSampleTableBoxes()
    {
        Mp4Box trak = CreateBox("trak");
        Mp4Box mdia = CreateBox("mdia");
        Mp4Box minf = CreateBox("minf");

        AddChild(trak, mdia);
        AddChild(mdia, minf);

        AddChild(minf, CreateBox("stbl"));
        AddChild(minf, CreateBox("stbl"));

        Assert.Throws<Mp4ParsingException>(
            () => _locator.Locate(trak));
    }

    private static Mp4Box CreateTrackTree()
    {
        Mp4Box trak = CreateBox("trak");
        Mp4Box mdia = CreateBox("mdia");
        Mp4Box minf = CreateBox("minf");
        Mp4Box stbl = CreateBox("stbl");

        AddChild(trak, mdia);
        AddChild(mdia, minf);
        AddChild(minf, stbl);

        return trak;
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

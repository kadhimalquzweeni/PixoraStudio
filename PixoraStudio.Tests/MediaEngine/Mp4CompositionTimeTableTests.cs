using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4CompositionTimeTableTests
{
    [Fact]
    public void Constructor_ShouldStoreEntries()
    {
        Mp4CompositionTimeEntry[] entries =
        [
            new(2, 100),
            new(3, -50)
        ];

        Mp4CompositionTimeTable table = new(entries);

        Assert.Equal(2U, table.EntryCount);
        Assert.Equal(2, table.Entries.Count);
    }

    [Fact]
    public void Constructor_ShouldDefensivelyCopyEntries()
    {
        List<Mp4CompositionTimeEntry> entries =
        [
            new(2, 100)
        ];

        Mp4CompositionTimeTable table = new(entries);

        entries.Add(new(3, -50));

        Assert.Single(table.Entries);
    }

    [Fact]
    public void Constructor_ShouldRejectNullEntries()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4CompositionTimeTable(null!));
    }

    [Fact]
    public void GetCompositionOffset_ShouldReturnOffsetForFirstSample()
    {
        Mp4CompositionTimeTable table = CreateTable();

        Assert.Equal(100, table.GetCompositionOffset(1));
    }

    [Fact]
    public void GetCompositionOffset_ShouldReturnOffsetForMultipleSamples()
    {
        Mp4CompositionTimeTable table = CreateTable();

        Assert.Equal(100, table.GetCompositionOffset(2));
        Assert.Equal(-50, table.GetCompositionOffset(3));
        Assert.Equal(-50, table.GetCompositionOffset(5));
        Assert.Equal(200, table.GetCompositionOffset(6));
        Assert.Equal(200, table.GetCompositionOffset(7));
    }

    [Fact]
    public void GetCompositionOffset_ShouldSupportNegativeOffsets()
    {
        Mp4CompositionTimeTable table =
            new([new Mp4CompositionTimeEntry(3, -100)]);

        Assert.Equal(-100, table.GetCompositionOffset(2));
    }

    [Fact]
    public void GetCompositionOffset_ShouldRejectZeroSampleNumber()
    {
        Mp4CompositionTimeTable table = CreateTable();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.GetCompositionOffset(0));
    }

    [Fact]
    public void GetCompositionOffset_ShouldRejectSampleNumberBeyondTable()
    {
        Mp4CompositionTimeTable table = CreateTable();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.GetCompositionOffset(8));
    }

    [Fact]
    public void GetCompositionOffset_ShouldRejectEmptyTableLookup()
    {
        Mp4CompositionTimeTable table =
            new(Array.Empty<Mp4CompositionTimeEntry>());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => table.GetCompositionOffset(1));
    }

    private static Mp4CompositionTimeTable CreateTable()
    {
        return new Mp4CompositionTimeTable(
        [
            new(2, 100),
            new(3, -50),
            new(2, 200)
        ]);
    }
}

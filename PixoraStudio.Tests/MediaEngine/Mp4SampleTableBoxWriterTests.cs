using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Readers.Mp4;
using PixoraStudio.MediaEngine.Processing.Export;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4SampleTableBoxWriterTests
{
    private readonly Mp4SampleTableBoxWriter _writer = new();

    [Fact]
    public void Write_ShouldCreateReadableSampleTablesForVideo()
    {
        Mp4TrimTrackPlan plan = CreatePlan(Mp4HandlerType.Video,
        [
            Sample(1, 0, 100, 4, true, 0),
            Sample(2, 100, 100, 5, false, 0),
            Sample(3, 200, 200, 6, true, 0)
        ]);

        using MemoryStream stream = new();
        _writer.Write(stream, plan, [1000, 1004, 1009]);

        stream.Position = 0;
        Mp4Box stbl = Assert.Single(new Mp4Reader().ReadBoxTree(stream));
        Mp4SampleTable table = new Mp4SampleTableReader().Read(stream, stbl);

        Assert.Equal(3u, table.SampleSizes.SampleCount);
        Assert.Equal(new uint[] { 4, 5, 6 }, table.SampleSizes.SampleSizes);
        Assert.Equal(new ulong[] { 1000, 1004, 1009 }, table.ChunkOffsets.Offsets);
        Assert.Equal(new uint[] { 1, 3 }, table.SyncSamples!.SampleNumbers);
        Assert.Equal(
            [new Mp4TimeToSampleEntry(2, 100), new Mp4TimeToSampleEntry(1, 200)],
            table.TimeToSampleEntries);
    }

    [Fact]
    public void Write_ShouldPreserveSignedCompositionOffsets()
    {
        Mp4TrimTrackPlan plan = CreatePlan(Mp4HandlerType.Video,
        [
            Sample(1, 0, 100, 4, true, 20),
            Sample(2, 100, 100, 4, false, -10)
        ]);

        using MemoryStream stream = new();
        _writer.Write(stream, plan, [1000, 1004]);

        stream.Position = 0;
        Mp4Box stbl = Assert.Single(new Mp4Reader().ReadBoxTree(stream));
        Mp4SampleTable table = new Mp4SampleTableReader().Read(stream, stbl);

        Assert.Equal(
            [new Mp4CompositionTimeEntry(1, 20), new Mp4CompositionTimeEntry(1, -10)],
            table.CompositionTimes!.Entries);
    }

    [Fact]
    public void Write_ShouldUse64BitChunkOffsetsWhenRequired()
    {
        Mp4TrimTrackPlan plan = CreatePlan(Mp4HandlerType.Audio,
        [
            Sample(1, 0, 100, 4, true, 0)
        ]);

        using MemoryStream stream = new();
        _writer.Write(stream, plan, [(ulong)uint.MaxValue + 1]);

        stream.Position = 0;
        Mp4Box box = Assert.Single(new Mp4Reader().ReadBoxTree(stream));
        Assert.Contains(box.Children, child => child.Type == "co64");
        Assert.DoesNotContain(box.Children, child => child.Type == "stco");
    }

    [Fact]
    public void Write_ShouldEncodeSampleDescriptionTransitionsInStsc()
    {
        Mp4TrimTrackPlan plan = CreatePlan(Mp4HandlerType.Audio,
        [
            Sample(1, 0, 100, 4, true, 0, 1),
            Sample(2, 100, 100, 4, true, 0, 1),
            Sample(3, 200, 100, 4, true, 0, 2)
        ]);

        using MemoryStream stream = new();
        _writer.Write(stream, plan, [1000, 1004, 1008]);

        stream.Position = 0;
        Mp4Box box = Assert.Single(new Mp4Reader().ReadBoxTree(stream));
        Mp4SampleTable table = new Mp4SampleTableReader().Read(stream, box);

        Assert.Equal(
            [new Mp4SampleToChunkEntry(1, 1, 1), new Mp4SampleToChunkEntry(3, 1, 2)],
            table.SampleToChunkEntries);
    }

    [Fact]
    public void Write_ShouldRejectWrongNumberOfChunkOffsets()
    {
        Mp4TrimTrackPlan plan = CreatePlan(Mp4HandlerType.Audio,
        [
            Sample(1, 0, 100, 4, true, 0)
        ]);

        using MemoryStream stream = new();
        Assert.Throws<ArgumentException>(() => _writer.Write(stream, plan, []));
    }

    [Fact]
    public void Write_ShouldRejectVideoWithoutSyncSamples()
    {
        Mp4TrimTrackPlan plan = CreatePlan(Mp4HandlerType.Video,
        [
            Sample(1, 0, 100, 4, false, 0)
        ]);

        using MemoryStream stream = new();
        Assert.Throws<Mp4ParsingException>(() => _writer.Write(stream, plan, [1000]));
    }

    private static Mp4TrimTrackPlan CreatePlan(
        Mp4HandlerType handlerType,
        IEnumerable<Mp4SampleLocation> samples) =>
        new(1, handlerType, samples);

    private static Mp4SampleLocation Sample(
        uint number,
        ulong decodeTime,
        uint duration,
        uint size,
        bool sync,
        int compositionOffset,
        uint descriptionIndex = 1) =>
        new(number, 1, 0, size, decodeTime, duration, sync)
        {
            CompositionOffsetTicks = compositionOffset,
            SampleDescriptionIndex = descriptionIndex
        };
}

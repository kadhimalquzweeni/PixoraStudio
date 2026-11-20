using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Export;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4TrimMoovWriterTests
{
    [Fact]
    public void Write_ShouldRebuildTrackTablesDurationsAndEditList()
    {
        using MemoryStream source = CreateSourceMoov(trackId: 1);
        Mp4Box moov = Assert.Single(new Mp4Reader().ReadBoxTree(source));
        (Mp4MediaInfo info, Mp4TrimExportPlan plan) = CreateVideoPlan();

        using MemoryStream output = new();
        new Mp4TrimMoovWriter().Write(
            source,
            output,
            moov,
            info,
            plan,
            new Dictionary<uint, IReadOnlyList<ulong>>
            {
                [1] = new ulong[] { 10_000, 10_004 }
            });

        output.Position = 0;
        Mp4Box rewrittenMoov = Assert.Single(new Mp4Reader().ReadBoxTree(output));

        Mp4Box mvhd = Assert.Single(rewrittenMoov.Children, box => box.Type == "mvhd");
        Assert.Equal(1000UL, new Mp4MovieHeaderReader().Read(output, mvhd).DurationTicks);

        Mp4Box trak = Assert.Single(rewrittenMoov.Children, box => box.Type == "trak");
        Mp4Box tkhd = Assert.Single(trak.Children, box => box.Type == "tkhd");
        Assert.Equal(1000UL, ReadDuration(output, tkhd, 20, 4));

        Mp4Box edts = Assert.Single(trak.Children, box => box.Type == "edts");
        Mp4EditList editList = new Mp4EditListReader().Read(
            output,
            Assert.Single(edts.Children));
        Assert.Equal(new[] { new Mp4EditListEntry(1000, 500, 1, 0) }, editList.Entries);

        Mp4Box mdia = Assert.Single(trak.Children, box => box.Type == "mdia");
        Mp4Box mdhd = Assert.Single(mdia.Children, box => box.Type == "mdhd");
        Assert.Equal(2000UL, new Mp4MediaHeaderReader().Read(output, mdhd).DurationTicks);

        Mp4Box minf = Assert.Single(mdia.Children, box => box.Type == "minf");
        Mp4Box stbl = Assert.Single(minf.Children, box => box.Type == "stbl");
        Assert.Contains(stbl.Children, box => box.Type == "stsd");
        Mp4SampleTable sampleTable = new Mp4SampleTableReader().Read(output, stbl);
        Assert.Equal(2u, sampleTable.SampleSizes.SampleCount);
        Assert.Equal(new uint[] { 1 }, sampleTable.SyncSamples!.SampleNumbers);
        Assert.Contains(stbl.Children, box => box.Type == "co64");
    }

    [Fact]
    public void Write_ShouldOmitTracksWithoutASelectedPlan()
    {
        using MemoryStream source = CreateSourceMoov(trackId: 1, includeSecondTrack: true);
        Mp4Box moov = Assert.Single(new Mp4Reader().ReadBoxTree(source));
        (Mp4MediaInfo info, Mp4TrimExportPlan plan) = CreateVideoPlan();

        using MemoryStream output = new();
        new Mp4TrimMoovWriter().Write(
            source,
            output,
            moov,
            info,
            plan,
            new Dictionary<uint, IReadOnlyList<ulong>>
            {
                [1] = new ulong[] { 10_000, 10_004 }
            });

        output.Position = 0;
        Mp4Box rewrittenMoov = Assert.Single(new Mp4Reader().ReadBoxTree(output));
        Assert.Single(rewrittenMoov.Children, box => box.Type == "trak");
    }

    [Fact]
    public void Write_ShouldUseAnEmptyEditWhenAnAudioTrackStartsAfterTheOutputOrigin()
    {
        using MemoryStream source = CreateSourceMoov(trackId: 2);
        Mp4Box moov = Assert.Single(new Mp4Reader().ReadBoxTree(source));
        Mp4MediaTrack track = CreateTrack(2, Mp4HandlerType.Audio,
        [
            Location(1, 2000, 1000, true)
        ]);
        Mp4PresentationSample presentation = new(2, 1, track.SampleLocations.GetSample(1), 2.0);
        Mp4MediaInfo info = new(
            new Mp4MovieHeader(1000, 5000),
            [track],
            [new Mp4MediaData(0, 100)],
            [presentation]);
        Mp4TrimTrackPlan trackPlan = new(2, Mp4HandlerType.Audio, track.SampleLocations.Samples);
        Mp4TrimExportPlan plan = new(2, 4, 1, 3, [trackPlan]);

        using MemoryStream output = new();
        new Mp4TrimMoovWriter().Write(
            source,
            output,
            moov,
            info,
            plan,
            new Dictionary<uint, IReadOnlyList<ulong>>
            {
                [2] = new ulong[] { 10_000 }
            });

        output.Position = 0;
        Mp4Box rewrittenMoov = Assert.Single(new Mp4Reader().ReadBoxTree(output));
        Mp4Box trak = Assert.Single(rewrittenMoov.Children, box => box.Type == "trak");
        Mp4Box edts = Assert.Single(trak.Children, box => box.Type == "edts");
        Mp4EditList editList = new Mp4EditListReader().Read(output, Assert.Single(edts.Children));

        Assert.Equal(
            new[]
            {
                new Mp4EditListEntry(1000, -1, 1, 0),
                new Mp4EditListEntry(1000, 0, 1, 0)
            },
            editList.Entries);
    }

    private static (Mp4MediaInfo Info, Mp4TrimExportPlan Plan) CreateVideoPlan()
    {
        Mp4MediaTrack track = CreateTrack(1, Mp4HandlerType.Video,
        [
            Location(1, 0, 1000, true),
            Location(2, 1000, 1000, false)
        ]);

        Mp4PresentationSample[] timeline =
        [
            new(1, 1, track.SampleLocations.GetSample(1), 0),
            new(1, 2, track.SampleLocations.GetSample(2), 1)
        ];

        Mp4MediaInfo info = new(
            new Mp4MovieHeader(1000, 3000),
            [track],
            [new Mp4MediaData(0, 100)],
            timeline);

        Mp4TrimTrackPlan trackPlan = new(1, Mp4HandlerType.Video, track.SampleLocations.Samples);
        Mp4TrimExportPlan plan = new(0.5, 1.5, 0.5, 1.5, [trackPlan]);
        return (info, plan);
    }

    private static Mp4MediaTrack CreateTrack(
        uint id,
        Mp4HandlerType type,
        IEnumerable<Mp4SampleLocation> samples)
    {
        Mp4SampleLocation[] locations = samples.ToArray();
        Mp4Track track = new(id, 640, 360, 1000, 5000, type,
            [new Mp4SampleDescription(1, type == Mp4HandlerType.Video ? "avc1" : "mp4a")]);
        Mp4SampleTable table = new(
            [new Mp4TimeToSampleEntry((uint)locations.Length, 1000)],
            [new Mp4SampleToChunkEntry(1, 1, 1)],
            new Mp4SampleSizeTable(0, (uint)locations.Length, locations.Select(sample => sample.Size)),
            new Mp4ChunkOffsetTable([0]),
            null);
        return new Mp4MediaTrack(track, new Mp4Box(8, "trak", 0, 8), table,
            new Mp4SampleLocationTable(locations));
    }

    private static Mp4SampleLocation Location(uint number, ulong dts, uint duration, bool sync) =>
        new(number, 1, (ulong)(number * 4), 4, dts, duration, sync)
        {
            SampleDescriptionIndex = 1
        };

    private static MemoryStream CreateSourceMoov(uint trackId, bool includeSecondTrack = false)
    {
        Mp4BoxWriter writer = new();
        MemoryStream stream = new();

        writer.WriteBox(stream, "moov", moov =>
        {
            writer.WriteBox(moov, "mvhd", payload =>
            {
                byte[] data = new byte[20];
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), 1000);
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), 5000);
                payload.Write(data);
            });

            WriteTrack(writer, moov, trackId);
            if (includeSecondTrack)
                WriteTrack(writer, moov, trackId + 1);
        });

        stream.Position = 0;
        return stream;
    }

    private static void WriteTrack(Mp4BoxWriter writer, Stream moov, uint trackId)
    {
        writer.WriteBox(moov, "trak", trak =>
        {
            writer.WriteBox(trak, "tkhd", payload =>
            {
                byte[] data = new byte[84];
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), trackId);
                payload.Write(data);
            });

            writer.WriteBox(trak, "mdia", mdia =>
            {
                writer.WriteBox(mdia, "mdhd", payload =>
                {
                    byte[] data = new byte[20];
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), 1000);
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), 5000);
                    payload.Write(data);
                });

                writer.WriteBox(mdia, "minf", minf =>
                    writer.WriteBox(minf, "stbl", stbl =>
                        writer.WriteBox(stbl, "stsd", payload =>
                            payload.Write(new byte[8]))));
            });
        });
    }

    private static ulong ReadDuration(Stream stream, Mp4Box box, int offset, int size)
    {
        long position = stream.Position;
        try
        {
            stream.Position = box.DataOffset + offset;
            byte[] bytes = new byte[size];
            stream.ReadExactly(bytes);
            return size == 4
                ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
                : BinaryPrimitives.ReadUInt64BigEndian(bytes);
        }
        finally
        {
            stream.Position = position;
        }
    }
}

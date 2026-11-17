
using System.Buffers.Binary;
using System.Reflection;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4TrackSamplePipelineReaderTests
{
    private readonly Mp4TrackSamplePipelineReader _reader = new();

    [Fact]
    public void Read_ShouldReturnAllEncodedSamples()
    {
        Mp4Box root = CreateValidFile(out MemoryStream stream);

        using (stream)
        {
            Mp4Box trackBox = root.Children
                .Single(box => box.Type == "moov")
                .Children.Single();

            Mp4Track track = CreateTrack();

            Mp4EncodedSample[] samples = _reader.Read(
                stream,
                root,
                trackBox,
                track).ToArray();

            Assert.Equal(3, samples.Length);

            Assert.Equal(
                new uint[] { 1, 2, 3 },
                samples.Select(sample => sample.Location.SampleNumber));

            Assert.Equal(new byte[] { 1, 2 }, samples[0].Data.ToArray());
            Assert.Equal(new byte[] { 3, 4 }, samples[1].Data.ToArray());
            Assert.Equal(new byte[] { 5, 6 }, samples[2].Data.ToArray());
        }
    }

    [Fact]
    public void Read_ShouldPreserveSampleTiming()
    {
        Mp4Box root = CreateValidFile(out MemoryStream stream);

        using (stream)
        {
            Mp4Box trackBox = root.Children
                .Single(box => box.Type == "moov")
                .Children.Single();

            Mp4EncodedSample[] samples = _reader.Read(
                stream,
                root,
                trackBox,
                CreateTrack()).ToArray();

            Assert.Equal(0UL, samples[0].Location.DecodeTimeTicks);
            Assert.Equal(100UL, samples[1].Location.DecodeTimeTicks);
            Assert.Equal(200UL, samples[2].Location.DecodeTimeTicks);

            Assert.Equal(100U, samples[0].Location.DurationTicks);
            Assert.Equal(100U, samples[1].Location.DurationTicks);
            Assert.Equal(100U, samples[2].Location.DurationTicks);
        }
    }

    [Fact]
    public void Read_ShouldPreserveSyncSampleInformation()
    {
        Mp4Box root = CreateValidFile(out MemoryStream stream);

        using (stream)
        {
            Mp4Box trackBox = root.Children
                .Single(box => box.Type == "moov")
                .Children.Single();

            Mp4EncodedSample[] samples = _reader.Read(
                stream,
                root,
                trackBox,
                CreateTrack()).ToArray();

            Assert.True(samples[0].Location.IsSyncSample);
            Assert.False(samples[1].Location.IsSyncSample);
            Assert.False(samples[2].Location.IsSyncSample);
        }
    }

    [Fact]
    public void Read_ShouldRejectMissingMediaData()
    {
        Mp4Box root = CreateValidFile(out MemoryStream stream);

        using (stream)
        {
            Mp4Box trackBox = root.Children
                .Single(box => box.Type == "moov")
                .Children.Single();

            Mp4Box rootWithoutMediaData = CreateBox("root");

            AddChild(rootWithoutMediaData, root.Children
                .Single(box => box.Type == "moov"));

            Assert.Throws<Mp4ParsingException>(
                () => _reader.Read(
                    stream,
                    rootWithoutMediaData,
                    trackBox,
                    CreateTrack()));
        }
    }

    [Fact]
    public void Read_ShouldRejectWrongTrackBoxType()
    {
        Mp4Box root = CreateValidFile(out MemoryStream stream);

        using (stream)
        {
            Assert.Throws<Mp4ParsingException>(
                () => _reader.Read(
                    stream,
                    root,
                    CreateBox("moov"),
                    CreateTrack()));
        }
    }

    [Fact]
    public void Read_ShouldRejectZeroTrackTimescale()
    {
        Mp4Box root = CreateValidFile(out MemoryStream stream);

        using (stream)
        {
            Mp4Box trackBox = root.Children
                .Single(box => box.Type == "moov")
                .Children.Single();

            Mp4Track invalidTrack = new(
                trackId: 1,
                width: 1920,
                height: 1080,
                timescale: 0);

            Assert.Throws<Mp4ParsingException>(
                () => _reader.Read(
                    stream,
                    root,
                    trackBox,
                    invalidTrack));
        }
    }

    [Fact]
    public void Read_ShouldRejectNullArguments()
    {
        using MemoryStream stream = new();

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                null!,
                CreateBox("root"),
                CreateBox("trak"),
                CreateTrack()));

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                stream,
                null!,
                CreateBox("trak"),
                CreateTrack()));

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                stream,
                CreateBox("root"),
                null!,
                CreateTrack()));

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                stream,
                CreateBox("root"),
                CreateBox("trak"),
                null!));
    }

    [Fact]
    public void Read_ShouldSupportCancellation()
    {
        Mp4Box root = CreateValidFile(out MemoryStream stream);

        using (stream)
        using (CancellationTokenSource cancellation = new())
        {
            Mp4Box trackBox = root.Children
                .Single(box => box.Type == "moov")
                .Children.Single();

            cancellation.Cancel();

            IEnumerable<Mp4EncodedSample> samples = _reader.Read(
                stream,
                root,
                trackBox,
                CreateTrack(),
                cancellation.Token);

            using IEnumerator<Mp4EncodedSample> iterator =
                samples.GetEnumerator();

            Assert.Throws<OperationCanceledException>(
                () => iterator.MoveNext());
        }
    }

    private static Mp4Track CreateTrack()
    {
        return new Mp4Track(
            trackId: 1,
            width: 1920,
            height: 1080,
            timescale: 1000,
            durationTicks: 300,
            handlerType: Mp4HandlerType.Video);
    }

    private static Mp4Box CreateValidFile(out MemoryStream stream)
    {
        stream = new MemoryStream();

        Mp4Box root = CreateBox("root");
        Mp4Box moov = CreateBox("moov");
        Mp4Box track = CreateBox("trak");
        Mp4Box mdia = CreateBox("mdia");
        Mp4Box minf = CreateBox("minf");
        Mp4Box stbl = CreateBox("stbl");

        AddChild(root, moov);
        AddChild(moov, track);
        AddChild(track, mdia);
        AddChild(mdia, minf);
        AddChild(minf, stbl);

        // Reserve space for the mdat header.
        long mdatOffset = stream.Position;
        stream.Write(new byte[8]);

        long sampleDataOffset = stream.Position;

        stream.Write(new byte[]
        {
            1, 2,
            3, 4,
            5, 6
        });

        long mdatSize = stream.Position - mdatOffset;

        Mp4Box mdat = new(
            size: checked((ulong)mdatSize),
            type: "mdat",
            offset: mdatOffset,
            headerSize: 8);

        AddChild(root, mdat);

        AddBox(stbl, stream, "stts",
            CreateBoxData(1, 3, 100));

        AddBox(stbl, stream, "stsc",
            CreateBoxData(1, 1, 3, 1));

        AddBox(stbl, stream, "stsz",
            CreateBoxData(0, 3, 2, 2, 2));

        AddBox(stbl, stream, "stco",
            CreateBoxData(1, checked((uint)sampleDataOffset)));

        AddBox(stbl, stream, "stss",
            CreateBoxData(1, 1));

        stream.Position = 0;

        return root;
    }

    private static void AddBox(
        Mp4Box parent,
        MemoryStream stream,
        string type,
        byte[] data)
    {
        long offset = stream.Position;

        stream.Write(new byte[8]);
        stream.Write(data);

        Mp4Box box = new(
            size: checked((ulong)(data.Length + 8)),
            type: type,
            offset: offset,
            headerSize: 8);

        AddChild(parent, box);
    }

    private static byte[] CreateBoxData(params uint[] values)
    {
        using MemoryStream stream = new();

        stream.Write(new byte[4]);

        Span<byte> buffer = stackalloc byte[4];

        foreach (uint value in values)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        return stream.ToArray();
    }

    private static Mp4Box CreateBox(string type)
    {
        return new Mp4Box(
            size: 8,
            type: type,
            offset: 0,
            headerSize: 8);
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

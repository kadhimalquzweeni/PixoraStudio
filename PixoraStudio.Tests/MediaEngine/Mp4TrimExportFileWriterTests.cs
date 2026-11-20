using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Processing.Export;
using PixoraStudio.MediaEngine.Readers.Mp4;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4TrimExportFileWriterTests
{
    [Fact]
    public void Export_ShouldRemuxSelectedGopIntoAReadableMp4WithoutChangingSampleBytes()
    {
        using MemoryStream source = CreateVideoMp4();
        using MemoryStream destination = new();

        Mp4TrimExportPlan plan = new Mp4TrimExportFileWriter().Export(
            source,
            destination,
            startSeconds: 2.2,
            endSeconds: 3.2);

        Assert.Equal(2.2, plan.RequestedStartSeconds);
        Assert.Equal(3.2, plan.RequestedEndSeconds);
        Assert.Equal(2.0, plan.EffectiveStartSeconds);
        Assert.Equal(4.0, plan.EffectiveEndSeconds);

        destination.Position = 0;
        Mp4MediaInfo output = new Mp4FileReader().Read(destination);
        Mp4MediaTrack video = Assert.Single(output.GetTracks(Mp4HandlerType.Video));
        Assert.Equal(2u, video.SampleCount);
        Assert.True(video.SampleLocations.GetSample(1).IsSyncSample);
        Assert.Equal(2.0, output.DurationSeconds);

        Mp4EncodedSample[] samples = video.ReadSamples(
            destination,
            output.MediaDataRegions).ToArray();

        Assert.Equal(new byte[] { 9, 10, 11, 12 }, samples[0].Data.ToArray());
        Assert.Equal(new byte[] { 13, 14, 15, 16 }, samples[1].Data.ToArray());

        IReadOnlyList<Mp4Box> topLevel = new Mp4Reader().ReadBoxTree(destination);
        Assert.Equal(
            new[] { "ftyp", "moov", "mdat" },
            topLevel.Select(box => box.Type));
    }

    [Fact]
    public void Export_ShouldRejectInvalidTrimRangeWithoutProducingAnOutputFile()
    {
        using MemoryStream source = CreateVideoMp4();
        using MemoryStream destination = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Mp4TrimExportFileWriter().Export(source, destination, 3, 2));

        Assert.Empty(destination.ToArray());
    }

    private static MemoryStream CreateVideoMp4()
    {
        Mp4BoxWriter writer = new();
        MemoryStream file = new();

        writer.WriteBox(file, "ftyp", payload =>
        {
            payload.Write(Encoding.ASCII.GetBytes("isom"));
            WriteUInt32(payload, 0);
            payload.Write(Encoding.ASCII.GetBytes("isom"));
        });

        byte[] samplePayload = Enumerable.Range(1, 20)
            .Select(value => (byte)value)
            .ToArray();

        writer.WriteBox(file, "mdat", payload => payload.Write(samplePayload));
        ulong firstSampleOffset = checked((ulong)file.Position - (ulong)samplePayload.Length);

        writer.WriteBox(file, "moov", moov =>
        {
            writer.WriteBox(moov, "mvhd", payload =>
            {
                byte[] data = new byte[20];
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), 1000);
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16, 4), 5000);
                payload.Write(data);
            });

            writer.WriteBox(moov, "trak", trak =>
            {
                writer.WriteBox(trak, "tkhd", payload =>
                {
                    byte[] data = new byte[84];
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12, 4), 1);
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20, 4), 5000);
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(76, 4), 640u << 16);
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(80, 4), 360u << 16);
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

                    writer.WriteBox(mdia, "hdlr", payload =>
                    {
                        byte[] data = new byte[12];
                        Encoding.ASCII.GetBytes("vide").CopyTo(data, 8);
                        payload.Write(data);
                    });

                    writer.WriteBox(mdia, "minf", minf =>
                    {
                        writer.WriteBox(minf, "stbl", stbl =>
                        {
                            WriteVideoSampleDescription(writer, stbl);

                            writer.WriteFullBox(stbl, "stts", 0, 0, body =>
                            {
                                WriteUInt32(body, 1);
                                WriteUInt32(body, 5);
                                WriteUInt32(body, 1000);
                            });

                            writer.WriteFullBox(stbl, "stsc", 0, 0, body =>
                            {
                                WriteUInt32(body, 1);
                                WriteUInt32(body, 1);
                                WriteUInt32(body, 5);
                                WriteUInt32(body, 1);
                            });

                            writer.WriteFullBox(stbl, "stsz", 0, 0, body =>
                            {
                                WriteUInt32(body, 0);
                                WriteUInt32(body, 5);
                                for (int i = 0; i < 5; i++)
                                    WriteUInt32(body, 4);
                            });

                            writer.WriteFullBox(stbl, "stco", 0, 0, body =>
                            {
                                WriteUInt32(body, 1);
                                WriteUInt32(body, checked((uint)firstSampleOffset));
                            });

                            writer.WriteFullBox(stbl, "stss", 0, 0, body =>
                            {
                                WriteUInt32(body, 3);
                                WriteUInt32(body, 1);
                                WriteUInt32(body, 3);
                                WriteUInt32(body, 5);
                            });
                        });
                    });
                });
            });
        });

        file.Position = 0;
        return file;
    }

    private static void WriteVideoSampleDescription(Mp4BoxWriter writer, Stream stbl)
    {
        writer.WriteFullBox(stbl, "stsd", 0, 0, body =>
        {
            WriteUInt32(body, 1);
            writer.WriteBox(body, "avc1", entry =>
            {
                byte[] visualEntry = new byte[78];
                BinaryPrimitives.WriteUInt16BigEndian(visualEntry.AsSpan(6, 2), 1);
                BinaryPrimitives.WriteUInt16BigEndian(visualEntry.AsSpan(24, 2), 640);
                BinaryPrimitives.WriteUInt16BigEndian(visualEntry.AsSpan(26, 2), 360);
                BinaryPrimitives.WriteUInt32BigEndian(visualEntry.AsSpan(28, 4), 0x00480000);
                BinaryPrimitives.WriteUInt32BigEndian(visualEntry.AsSpan(32, 4), 0x00480000);
                BinaryPrimitives.WriteUInt16BigEndian(visualEntry.AsSpan(40, 2), 1);
                BinaryPrimitives.WriteUInt16BigEndian(visualEntry.AsSpan(74, 2), 24);
                entry.Write(visualEntry);
                writer.WriteBox(entry, "avcC", configuration =>
                    configuration.Write(new byte[] { 1, 66, 0, 30, 0xFF, 0xE0, 0 }));
            });
        });
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}

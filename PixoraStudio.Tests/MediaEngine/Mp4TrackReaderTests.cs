
using System.Buffers.Binary;
using System.Text;
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Readers.Mp4;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4TrackReaderTests
{
    private readonly Mp4TrackReader _reader = new();

    [Fact]
    public void Read_ShouldExtractVideoTrack()
    {
        byte[] tkhd = CreateTkhdBox(
            trackId: 1,
            width: 1920,
            height: 1080);

        byte[] mdhd = CreateMdhdBox(
            timescale: 1000,
            duration: 120000);

        byte[] hdlr = CreateHdlrBox("vide");

        byte[] mdia = CreateBox(
            "mdia",
            Combine(mdhd, hdlr));

        byte[] trak = CreateBox(
            "trak",
            Combine(tkhd, mdia));

        byte[] moov = CreateBox("moov", trak);

        using MemoryStream stream = new(moov);

        Mp4Reader mp4Reader = new();

        IReadOnlyList<Mp4Box> boxes =
            mp4Reader.ReadBoxTree(stream);

        IReadOnlyList<Mp4Track> tracks =
            _reader.Read(stream, boxes);

        Mp4Track track = Assert.Single(tracks);

        Assert.Equal(1u, track.TrackId);
        Assert.Equal(Mp4HandlerType.Video, track.HandlerType);
        Assert.Equal(1920, track.Width);
        Assert.Equal(1080, track.Height);
        Assert.Equal(1000u, track.Timescale);
        Assert.Equal(120000ul, track.DurationTicks);
        Assert.Equal(120, track.DurationSeconds);
    }

    [Fact]
    public void Read_ShouldExtractAudioTrack()
    {
        byte[] tkhd = CreateTkhdBox(
            trackId: 2,
            width: 0,
            height: 0);

        byte[] mdhd = CreateMdhdBox(
            timescale: 48000,
            duration: 960000);

        byte[] hdlr = CreateHdlrBox("soun");

        byte[] mdia = CreateBox(
            "mdia",
            Combine(mdhd, hdlr));

        byte[] trak = CreateBox(
            "trak",
            Combine(tkhd, mdia));

        byte[] moov = CreateBox("moov", trak);

        using MemoryStream stream = new(moov);

        Mp4Reader mp4Reader = new();

        IReadOnlyList<Mp4Box> boxes =
            mp4Reader.ReadBoxTree(stream);

        IReadOnlyList<Mp4Track> tracks =
            _reader.Read(stream, boxes);

        Mp4Track track = Assert.Single(tracks);

        Assert.Equal(2u, track.TrackId);
        Assert.Equal(Mp4HandlerType.Audio, track.HandlerType);
        Assert.Equal(48000u, track.Timescale);
        Assert.Equal(960000ul, track.DurationTicks);
        Assert.Equal(20, track.DurationSeconds);
    }

    [Fact]
    public void Read_ShouldRejectMissingMovieBox()
    {
        byte[] data = CreateBox("free");

        using MemoryStream stream = new(data);

        Mp4Reader mp4Reader = new();

        IReadOnlyList<Mp4Box> boxes =
            mp4Reader.ReadBoxTree(stream);

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(stream, boxes));
    }

    private static byte[] CreateTkhdBox(
        uint trackId,
        double width,
        double height)
    {
        byte[] payload = new byte[84];

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(12, 4),
            trackId);

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(76, 4),
            checked((uint)(width * 65536)));

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(80, 4),
            checked((uint)(height * 65536)));

        return CreateBox("tkhd", payload);
    }

    private static byte[] CreateMdhdBox(
        uint timescale,
        uint duration)
    {
        byte[] payload = new byte[20];

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(12, 4),
            timescale);

        BinaryPrimitives.WriteUInt32BigEndian(
            payload.AsSpan(16, 4),
            duration);

        return CreateBox("mdhd", payload);
    }

    private static byte[] CreateHdlrBox(string handler)
    {
        byte[] payload = new byte[12];

        Encoding.ASCII.GetBytes(handler)
            .CopyTo(payload, 8);

        return CreateBox("hdlr", payload);
    }

    private static byte[] CreateBox(
        string type,
        byte[]? payload = null)
    {
        payload ??= [];

        byte[] data = new byte[8 + payload.Length];

        BinaryPrimitives.WriteUInt32BigEndian(
            data.AsSpan(0, 4),
            (uint)data.Length);

        Encoding.ASCII.GetBytes(type)
            .CopyTo(data, 4);

        payload.CopyTo(data, 8);

        return data;
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        return arrays.SelectMany(array => array).ToArray();
    }
}

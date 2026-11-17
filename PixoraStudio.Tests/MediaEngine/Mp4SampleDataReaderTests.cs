
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4SampleDataReaderTests
{
    private readonly Mp4SampleDataReader _reader = new();

    [Fact]
    public void Read_ShouldReadSampleBytes()
    {
        byte[] fileData = CreateFileData();

        using MemoryStream stream = new(fileData);

        Mp4SampleLocation sample = CreateSample(
            offset: 110,
            size: 4);

        byte[] result = _reader.Read(
            stream,
            sample,
            CreateMediaDataRegions());

        Assert.Equal(
            new byte[] { 1, 2, 3, 4 },
            result);
    }

    [Fact]
    public void Read_ShouldRestoreOriginalStreamPosition()
    {
        using MemoryStream stream = new(CreateFileData());

        stream.Position = 5;

        _reader.Read(
            stream,
            CreateSample(110, 4),
            CreateMediaDataRegions());

        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void Read_ShouldRejectSampleOutsideMediaData()
    {
        using MemoryStream stream = new(CreateFileData());

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(
                stream,
                CreateSample(50, 4),
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectSampleExtendingBeyondMediaData()
    {
        using MemoryStream stream = new(CreateFileData());

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(
                stream,
                CreateSample(195, 10),
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectSampleBeyondStreamLength()
    {
        using MemoryStream stream = new(CreateFileData());

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(
                stream,
                CreateSample(198, 4),
                [new Mp4MediaData(100, 200)]));
    }

    [Fact]
    public void Read_ShouldSupportEmptySample()
    {
        using MemoryStream stream = new(CreateFileData());

        byte[] result = _reader.Read(
            stream,
            CreateSample(110, 0),
            CreateMediaDataRegions());

        Assert.Empty(result);
    }

    [Fact]
    public void Read_ShouldRejectMissingMediaDataRegions()
    {
        using MemoryStream stream = new(CreateFileData());

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(
                stream,
                CreateSample(110, 4),
                []));
    }

    [Fact]
    public void Read_ShouldRejectNullStream()
    {
        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                null!,
                CreateSample(110, 4),
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectNullSample()
    {
        using MemoryStream stream = new(CreateFileData());

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                stream,
                null!,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectNullMediaDataRegions()
    {
        using MemoryStream stream = new(CreateFileData());

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                stream,
                CreateSample(110, 4),
                null!));
    }

    private static Mp4SampleLocation CreateSample(
        ulong offset,
        uint size)
    {
        return new Mp4SampleLocation(
            1,
            1,
            offset,
            size,
            0,
            100,
            true);
    }

    private static IReadOnlyList<Mp4MediaData> CreateMediaDataRegions()
    {
        return
        [
            new Mp4MediaData(108, 92)
        ];
    }

    private static byte[] CreateFileData()
    {
        byte[] data = new byte[200];

        data[110] = 1;
        data[111] = 2;
        data[112] = 3;
        data[113] = 4;

        return data;
    }
}

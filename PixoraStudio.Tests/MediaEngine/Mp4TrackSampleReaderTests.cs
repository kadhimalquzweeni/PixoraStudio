
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4TrackSampleReaderTests
{
    private readonly Mp4TrackSampleReader _reader = new();

    [Fact]
    public void Read_ShouldReturnEncodedSample()
    {
        using MemoryStream stream = CreateStream();

        Mp4SampleLocationTable locations =
            CreateSampleLocations();

        Mp4EncodedSample result = _reader.Read(
            stream,
            locations,
            1,
            CreateMediaDataRegions());

        Assert.Equal(1U, result.Location.SampleNumber);

        Assert.Equal(
            new byte[] { 1, 2, 3, 4 },
            result.Data.ToArray());
    }

    [Fact]
    public void Read_ShouldReturnCorrectSampleByNumber()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample result = _reader.Read(
            stream,
            CreateSampleLocations(),
            2,
            CreateMediaDataRegions());

        Assert.Equal(2U, result.Location.SampleNumber);

        Assert.Equal(
            new byte[] { 5, 6, 7, 8 },
            result.Data.ToArray());
    }

    [Fact]
    public void Read_ShouldPreserveSampleTimingInformation()
    {
        using MemoryStream stream = CreateStream();

        Mp4SampleLocation location = new(
            1, 1, 110, 4, 100, 50, true)
        {
            CompositionOffsetTicks = 25
        };

        Mp4SampleLocationTable locations =
            new([location]);

        Mp4EncodedSample result = _reader.Read(
            stream,
            locations,
            1,
            CreateMediaDataRegions());

        Assert.Equal(100UL, result.Location.DecodeTimeTicks);
        Assert.Equal(50U, result.Location.DurationTicks);
        Assert.Equal(125L, result.Location.PresentationTimeTicks);
        Assert.True(result.Location.IsSyncSample);
    }

    [Fact]
    public void Read_ShouldRejectInvalidSampleNumber()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(
                stream,
                CreateSampleLocations(),
                0,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectSampleNumberAboveCount()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<Mp4ParsingException>(
            () => _reader.Read(
                stream,
                CreateSampleLocations(),
                3,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectNullStream()
    {
        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                null!,
                CreateSampleLocations(),
                1,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectNullLocations()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                stream,
                null!,
                1,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Read_ShouldRejectNullMediaDataRegions()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(
                stream,
                CreateSampleLocations(),
                1,
                null!));
    }

    private static Mp4SampleLocationTable CreateSampleLocations()
    {
        return new Mp4SampleLocationTable(
        [
            new(1, 1, 110, 4, 0, 100, true),
            new(2, 1, 114, 4, 100, 100, false)
        ]);
    }

    private static IReadOnlyList<Mp4MediaData> CreateMediaDataRegions()
    {
        return
        [
            new Mp4MediaData(108, 92)
        ];
    }

    private static MemoryStream CreateStream()
    {
        byte[] data = new byte[200];

        data[110] = 1;
        data[111] = 2;
        data[112] = 3;
        data[113] = 4;

        data[114] = 5;
        data[115] = 6;
        data[116] = 7;
        data[117] = 8;

        return new MemoryStream(data);
    }
}

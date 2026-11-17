
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Exceptions;
using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4TrackSampleEnumeratorTests
{
    private readonly Mp4TrackSampleEnumerator _enumerator = new();

    [Fact]
    public void Enumerate_ShouldReturnAllSamples()
    {
        using MemoryStream stream = CreateStream();

        IEnumerable<Mp4EncodedSample> samples =
            _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                CreateMediaDataRegions());

        Assert.Equal(3, samples.Count());
    }

    [Fact]
    public void Enumerate_ShouldPreserveSampleOrder()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample[] samples = _enumerator.Enumerate(
            stream,
            CreateSampleLocations(),
            CreateMediaDataRegions()).ToArray();

        Assert.Equal(
            new uint[] { 1, 2, 3 },
            samples.Select(sample => sample.Location.SampleNumber));
    }

    [Fact]
    public void Enumerate_ShouldReadCorrectSampleData()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample[] samples = _enumerator.Enumerate(
            stream,
            CreateSampleLocations(),
            CreateMediaDataRegions()).ToArray();

        Assert.Equal(new byte[] { 1, 2 }, samples[0].Data.ToArray());
        Assert.Equal(new byte[] { 3, 4 }, samples[1].Data.ToArray());
        Assert.Equal(new byte[] { 5, 6 }, samples[2].Data.ToArray());
    }

    [Fact]
    public void Enumerate_ShouldPreserveTimingInformation()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample[] samples = _enumerator.Enumerate(
            stream,
            CreateSampleLocations(),
            CreateMediaDataRegions()).ToArray();

        Assert.Equal(0UL, samples[0].Location.DecodeTimeTicks);
        Assert.Equal(100UL, samples[1].Location.DecodeTimeTicks);
        Assert.Equal(200UL, samples[2].Location.DecodeTimeTicks);

        Assert.Equal(100U, samples[0].Location.DurationTicks);
        Assert.Equal(100U, samples[1].Location.DurationTicks);
        Assert.Equal(100U, samples[2].Location.DurationTicks);
    }

    [Fact]
    public void Enumerate_ShouldPreserveSyncSampleInformation()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample[] samples = _enumerator.Enumerate(
            stream,
            CreateSampleLocations(),
            CreateMediaDataRegions()).ToArray();

        Assert.True(samples[0].Location.IsSyncSample);
        Assert.False(samples[1].Location.IsSyncSample);
        Assert.False(samples[2].Location.IsSyncSample);
    }

    [Fact]
    public void Enumerate_ShouldReturnEmptySequenceForEmptyTrack()
    {
        using MemoryStream stream = CreateStream();

        Mp4SampleLocationTable locations = new([]);

        IEnumerable<Mp4EncodedSample> samples =
            _enumerator.Enumerate(
                stream,
                locations,
                CreateMediaDataRegions());

        Assert.Empty(samples);
    }

    [Fact]
    public void Enumerate_ShouldRejectNullStream()
    {
        Assert.Throws<ArgumentNullException>(
            () => _enumerator.Enumerate(
                null!,
                CreateSampleLocations(),
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Enumerate_ShouldRejectNullLocations()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<ArgumentNullException>(
            () => _enumerator.Enumerate(
                stream,
                null!,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Enumerate_ShouldRejectNullMediaDataRegions()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<ArgumentNullException>(
            () => _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                null!));
    }

    private static Mp4SampleLocationTable CreateSampleLocations()
    {
        return new Mp4SampleLocationTable(
        [
            new(1, 1, 110, 2, 0, 100, true),
            new(2, 1, 112, 2, 100, 100, false),
            new(3, 1, 114, 2, 200, 100, false)
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

        return new MemoryStream(data);
    }

    [Fact]
    public void Enumerate_ShouldThrowWhenAlreadyCancelled()
    {
        using MemoryStream stream = CreateStream();
        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        IEnumerable<Mp4EncodedSample> samples =
            _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                CreateMediaDataRegions(),
                cancellation.Token);

        using IEnumerator<Mp4EncodedSample> iterator =
            samples.GetEnumerator();

        Assert.Throws<OperationCanceledException>(
            () => iterator.MoveNext());
    }

    [Fact]
    public void Enumerate_ShouldStopWhenCancelledBetweenSamples()
    {
        using MemoryStream stream = CreateStream();
        using CancellationTokenSource cancellation = new();

        IEnumerable<Mp4EncodedSample> samples =
            _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                CreateMediaDataRegions(),
                cancellation.Token);

        using IEnumerator<Mp4EncodedSample> iterator =
            samples.GetEnumerator();

        Assert.True(iterator.MoveNext());
        Assert.Equal(1U, iterator.Current.Location.SampleNumber);

        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => iterator.MoveNext());
    }

    [Fact]
    public void Enumerate_ShouldCompleteWhenNotCancelled()
    {
        using MemoryStream stream = CreateStream();
        using CancellationTokenSource cancellation = new();

        IEnumerable<Mp4EncodedSample> samples =
            _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                CreateMediaDataRegions(),
                cancellation.Token);

        Assert.Equal(3, samples.Count());
    }

    [Fact]
    public void Enumerate_ShouldReadSelectedSampleRange()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample[] samples = _enumerator.Enumerate(
            stream,
            CreateSampleLocations(),
            2,
            2,
            CreateMediaDataRegions()).ToArray();

        Assert.Equal(2, samples.Length);

        Assert.Equal(2U, samples[0].Location.SampleNumber);
        Assert.Equal(3U, samples[1].Location.SampleNumber);

        Assert.Equal(new byte[] { 3, 4 }, samples[0].Data.ToArray());
        Assert.Equal(new byte[] { 5, 6 }, samples[1].Data.ToArray());
    }

    [Fact]
    public void Enumerate_ShouldReadSingleSampleRange()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample[] samples = _enumerator.Enumerate(
            stream,
            CreateSampleLocations(),
            2,
            1,
            CreateMediaDataRegions()).ToArray();

        Assert.Single(samples);
        Assert.Equal(2U, samples[0].Location.SampleNumber);
    }

    [Fact]
    public void Enumerate_ShouldAllowRangeEndingAtLastSample()
    {
        using MemoryStream stream = CreateStream();

        Mp4EncodedSample[] samples = _enumerator.Enumerate(
            stream,
            CreateSampleLocations(),
            2,
            2,
            CreateMediaDataRegions()).ToArray();

        Assert.Equal(3U, samples[^1].Location.SampleNumber);
    }

    [Fact]
    public void Enumerate_ShouldRejectZeroStartingSampleNumber()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<Mp4ParsingException>(
            () => _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                0,
                1,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Enumerate_ShouldRejectZeroSampleCount()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<Mp4ParsingException>(
            () => _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                1,
                0,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Enumerate_ShouldRejectStartingSampleAboveTrackCount()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<Mp4ParsingException>(
            () => _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                4,
                1,
                CreateMediaDataRegions()));
    }

    [Fact]
    public void Enumerate_ShouldRejectRangeExceedingTrackCount()
    {
        using MemoryStream stream = CreateStream();

        Assert.Throws<Mp4ParsingException>(
            () => _enumerator.Enumerate(
                stream,
                CreateSampleLocations(),
                2,
                3,
                CreateMediaDataRegions()));
    }


}

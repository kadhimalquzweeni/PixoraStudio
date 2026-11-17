
using PixoraStudio.MediaEngine.Containers.Mp4;
using PixoraStudio.MediaEngine.Models.General;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class Mp4MediaInfoTests
{
    [Fact]
    public void GetTrack_ShouldReturnMatchingTrack()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        Mp4MediaTrack? result = mediaInfo.GetTrack(2);

        Assert.NotNull(result);
        Assert.Equal(2u, result.TrackId);
        Assert.Equal(Mp4HandlerType.Audio, result.HandlerType);
    }

    [Fact]
    public void GetTrack_ShouldReturnNullWhenTrackDoesNotExist()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        Assert.Null(mediaInfo.GetTrack(99));
    }

    [Fact]
    public void GetTracks_ShouldReturnOnlyMatchingHandlerType()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        IReadOnlyList<Mp4MediaTrack> result =
            mediaInfo.GetTracks(Mp4HandlerType.Video);

        Assert.Single(result);
        Assert.Equal(1u, result[0].TrackId);
    }

    [Fact]
    public void GetTracks_ShouldReturnEmptyWhenNoTracksMatch()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        IReadOnlyList<Mp4MediaTrack> result =
            mediaInfo.GetTracks(Mp4HandlerType.Subtitle);

        Assert.Empty(result);
    }

    [Fact]
    public void Constructor_ShouldPreserveMovieDuration()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        Assert.Equal(2.0, mediaInfo.DurationSeconds);
        Assert.Equal(2000ul, mediaInfo.MovieHeader.DurationTicks);
    }

    [Fact]
    public void Constructor_ShouldCopyTrackCollection()
    {
        List<Mp4MediaTrack> tracks = [CreateTrack(1, Mp4HandlerType.Video)];

        Mp4MediaInfo mediaInfo = new(
            new Mp4MovieHeader(1000, 2000),
            tracks,
            [],
            []);

        tracks.Clear();

        Assert.Single(mediaInfo.Tracks);
    }

    [Fact]
    public void Constructor_ShouldCopyPresentationSamples()
    {
        List<Mp4PresentationSample> samples =
        [
            CreatePresentationSample(1, 1, 0)
        ];

        Mp4MediaInfo mediaInfo = new(
            new Mp4MovieHeader(1000, 2000),
            [CreateTrack(1, Mp4HandlerType.Video)],
            [],
            samples);

        samples.Clear();

        Assert.Single(mediaInfo.PresentationSamples);
    }

    [Fact]
    public void Constructor_ShouldRejectNullMovieHeader()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4MediaInfo(
                null!,
                [],
                [],
                []));
    }

    [Fact]
    public void Constructor_ShouldRejectNullTracks()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4MediaInfo(
                new Mp4MovieHeader(1000, 2000),
                null!,
                [],
                []));
    }

    [Fact]
    public void Constructor_ShouldRejectNullMediaDataRegions()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4MediaInfo(
                new Mp4MovieHeader(1000, 2000),
                [],
                null!,
                []));
    }

    [Fact]
    public void Constructor_ShouldRejectNullPresentationSamples()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4MediaInfo(
                new Mp4MovieHeader(1000, 2000),
                [],
                [],
                null!));
    }

    [Fact]
    public void ReadPresentationSamples_ShouldRejectNullStream()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        Assert.Throws<ArgumentNullException>(
            () => mediaInfo.ReadPresentationSamples(null!));
    }

    [Fact]
    public void ReadPresentationSamples_ShouldRejectNonSeekableStream()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        using MemoryStream inner = new();
        using NonSeekableStream stream = new(inner);

        Assert.Throws<ArgumentException>(
            () => mediaInfo.ReadPresentationSamples(stream));
    }

    [Fact]
    public void ReadPresentationSamples_ShouldRejectUnreadableStream()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo();

        using MemoryStream inner = new();
        using NonReadableStream stream = new(inner);

        Assert.Throws<ArgumentException>(
            () => mediaInfo.ReadPresentationSamples(stream));
    }

    [Fact]
    public void ReadPresentationSamples_ShouldRestoreStreamPosition()
    {
        byte[] fileData = new byte[128];

        Mp4SampleLocation location = CreateLocation(
            sampleNumber: 1,
            offset: 20,
            size: 4);

        Mp4MediaInfo mediaInfo = CreateMediaInfo(
            [location],
            [new Mp4MediaData(20, 4)]);

        fileData[20] = 1;
        fileData[21] = 2;
        fileData[22] = 3;
        fileData[23] = 4;

        using MemoryStream stream = new(fileData);
        stream.Position = 5;

        Mp4PlaybackSample[] samples =
            mediaInfo.ReadPresentationSamples(stream).ToArray();

        Assert.Single(samples);
        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void ReadPresentationSamples_ShouldReturnCorrectEncodedData()
    {
        byte[] fileData = new byte[128];

        byte[] expected = [10, 20, 30, 40];

        Array.Copy(expected, 0, fileData, 20, expected.Length);

        Mp4MediaInfo mediaInfo = CreateMediaInfo(
            [
                CreateLocation(1, 20, 4)
            ],
            [new Mp4MediaData(20, 4)]);

        using MemoryStream stream = new(fileData);

        Mp4PlaybackSample sample =
            Assert.Single(mediaInfo.ReadPresentationSamples(stream));

        Assert.Equal(expected, sample.Sample.Data.ToArray());
        Assert.Equal(1u, sample.TrackId);
        Assert.Equal(Mp4HandlerType.Video, sample.HandlerType);
    }

    [Fact]
    public void ReadPresentationSamples_ShouldRejectSampleOutsideMediaData()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo(
            [
                CreateLocation(1, 50, 4)
            ],
            [new Mp4MediaData(20, 4)]);

        using MemoryStream stream = new(new byte[128]);

        Assert.Throws<InvalidOperationException>(
            () => mediaInfo.ReadPresentationSamples(stream).ToArray());
    }

    [Fact]
    public void ReadPresentationSamples_ShouldRejectSampleBeyondStream()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo(
            [
                CreateLocation(1, 120, 16)
            ],
            [new Mp4MediaData(120, 16)]);

        using MemoryStream stream = new(new byte[128]);

        Assert.Throws<InvalidOperationException>(
            () => mediaInfo.ReadPresentationSamples(stream).ToArray());
    }

    [Fact]
    public void ReadPresentationSamples_ShouldSupportCancellation()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo(
            [
                CreateLocation(1, 20, 4),
                CreateLocation(2, 24, 4)
            ],
            [new Mp4MediaData(20, 8)]);

        using MemoryStream stream = new(new byte[128]);

        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => mediaInfo.ReadPresentationSamples(
                stream,
                cancellation.Token).ToArray());
    }

    [Fact]
    public void ReadPresentationSamples_ShouldRestorePositionAfterCancellation()
    {
        Mp4MediaInfo mediaInfo = CreateMediaInfo(
            [
                CreateLocation(1, 20, 4)
            ],
            [new Mp4MediaData(20, 4)]);

        using MemoryStream stream = new(new byte[128]);

        stream.Position = 7;

        using CancellationTokenSource cancellation = new();

        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => mediaInfo.ReadPresentationSamples(
                stream,
                cancellation.Token).ToArray());

        Assert.Equal(7, stream.Position);
    }

    private static Mp4MediaInfo CreateMediaInfo(
        IEnumerable<Mp4SampleLocation>? locations = null,
        IEnumerable<Mp4MediaData>? mediaData = null)
    {
        Mp4MediaTrack track = CreateTrack(
            1,
            Mp4HandlerType.Video,
            locations);

        Mp4PresentationSample[] presentationSamples =
            track.SampleLocations.Samples
                .Select(location =>
                    CreatePresentationSample(
                        track.TrackId,
                        location.SampleNumber,
                        (double)location.PresentationTimeTicks /
                        track.Timescale,
                        location))
                .ToArray();

        return new Mp4MediaInfo(
            new Mp4MovieHeader(1000, 2000),
            [
                track,
                CreateTrack(2, Mp4HandlerType.Audio)
            ],
            mediaData ?? [new Mp4MediaData(0, 128)],
            presentationSamples);
    }

    private static Mp4MediaTrack CreateTrack(
        uint trackId,
        Mp4HandlerType handlerType,
        IEnumerable<Mp4SampleLocation>? locations = null)
    {
        Mp4Track track = new(
            trackId,
            1920,
            1080,
            1000,
            2000,
            handlerType,
            [
                new Mp4SampleDescription(1, "avc1")
            ]);

        Mp4SampleTable sampleTable = new(
            [],
            [],
            new Mp4SampleSizeTable(1, 0, []),
            new Mp4ChunkOffsetTable([]),
            null);

        return new Mp4MediaTrack(
            track,
            new Mp4Box(8, "trak", 0, 8),
            sampleTable,
            new Mp4SampleLocationTable(
                locations ?? []));
    }

    private static Mp4SampleLocation CreateLocation(
        uint sampleNumber,
        ulong offset,
        uint size)
    {
        return new Mp4SampleLocation(
            sampleNumber,
            1,
            offset,
            size,
            0,
            100,
            true)
        {
            SampleDescriptionIndex = 1
        };
    }

    private static Mp4PresentationSample CreatePresentationSample(
        uint trackId,
        uint sampleNumber,
        double presentationTime,
        Mp4SampleLocation? location = null)
    {
        return new Mp4PresentationSample(
            trackId,
            sampleNumber,
            location ?? CreateLocation(sampleNumber, 0, 1),
            presentationTime);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class NonReadableStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

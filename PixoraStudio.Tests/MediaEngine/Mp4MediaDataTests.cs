using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4MediaDataTests
{
    [Fact]
    public void Constructor_ShouldStoreMediaDataLocation()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.Equal(108, mediaData.DataOffset);
        Assert.Equal(992, mediaData.DataSize);
        Assert.Equal(1100, mediaData.EndOffset);
    }

    [Fact]
    public void Constructor_ShouldAllowEmptyMediaData()
    {
        Mp4MediaData mediaData = new(108, 0);

        Assert.Equal(108, mediaData.DataOffset);
        Assert.Equal(0, mediaData.DataSize);
        Assert.Equal(108, mediaData.EndOffset);
    }

    [Fact]
    public void Constructor_ShouldRejectNegativeOffset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Mp4MediaData(-1, 100));
    }

    [Fact]
    public void Constructor_ShouldRejectNegativeSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Mp4MediaData(100, -1));
    }

    [Fact]
    public void Constructor_ShouldRejectEndOffsetOverflow()
    {
        Assert.Throws<OverflowException>(
            () => new Mp4MediaData(long.MaxValue, 1));
    }

    [Fact]
    public void Contains_ShouldAcceptRangeInsideMediaData()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.True(mediaData.Contains(200, 100));
    }

    [Fact]
    public void Contains_ShouldAcceptRangeAtStart()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.True(mediaData.Contains(108, 100));
    }

    [Fact]
    public void Contains_ShouldAcceptRangeEndingAtBoundary()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.True(mediaData.Contains(1090, 10));
    }

    [Fact]
    public void Contains_ShouldRejectRangeBeforeMediaData()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.False(mediaData.Contains(100, 10));
    }

    [Fact]
    public void Contains_ShouldRejectRangeAfterMediaData()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.False(mediaData.Contains(1100, 1));
    }

    [Fact]
    public void Contains_ShouldRejectRangeExtendingBeyondBoundary()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.False(mediaData.Contains(1090, 11));
    }

    [Fact]
    public void Contains_ShouldAcceptEmptyRangeAtEnd()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.True(mediaData.Contains(1100, 0));
    }

    [Fact]
    public void Contains_ShouldRejectOffsetAboveLongMaxValue()
    {
        Mp4MediaData mediaData = new(108, 992);

        Assert.False(mediaData.Contains(
            (ulong)long.MaxValue + 1,
            1));
    }
}

using PixoraStudio.MediaEngine.Models.General;

namespace PixoraStudio.Tests.MediaEngine;

public class Mp4EncodedSampleTests
{
    [Fact]
    public void Constructor_ShouldStoreLocationAndData()
    {
        Mp4SampleLocation location = CreateLocation();
        byte[] data = [1, 2, 3, 4];

        Mp4EncodedSample result = new(location, data);

        Assert.Same(location, result.Location);
        Assert.Equal(data, result.Data.ToArray());
    }

    [Fact]
    public void Constructor_ShouldCopyData()
    {
        byte[] data = [1, 2, 3];

        Mp4EncodedSample result = new(CreateLocation(), data);

        data[0] = 99;

        Assert.Equal(
            new byte[] { 1, 2, 3 },
            result.Data.ToArray());
    }

    [Fact]
    public void Constructor_ShouldRejectNullLocation()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4EncodedSample(null!, []));
    }

    [Fact]
    public void Constructor_ShouldRejectNullData()
    {
        Assert.Throws<ArgumentNullException>(
            () => new Mp4EncodedSample(CreateLocation(), null!));
    }

    private static Mp4SampleLocation CreateLocation()
    {
        return new Mp4SampleLocation(
            1, 1, 100, 4, 0, 100, true);
    }
}

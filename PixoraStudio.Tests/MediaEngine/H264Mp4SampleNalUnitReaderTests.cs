using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4SampleNalUnitReaderTests
{
    [Fact]
    public void Read_ShouldSplitSampleUsingAvccLengthSize()
    {
        H264AvcDecoderConfiguration configuration = new(
            66, 0, 30, 2, [], [], []);
        Mp4EncodedSample sample = new(
            Location(),
            [0, 2, 0x65, 0x80, 0, 2, 0x41, 0x80]);

        IReadOnlyList<H264NalUnit> units =
            new H264Mp4SampleNalUnitReader().Read(sample, configuration);

        Assert.Equal(2, units.Count);
        Assert.Equal((byte)5, units[0].NalUnitType);
        Assert.Equal((byte)1, units[1].NalUnitType);
        Assert.Equal(new byte[] { 0x80 }, units[0].Payload.ToArray());
        Assert.Equal(new byte[] { 0x80 }, units[1].Payload.ToArray());
    }

    [Fact]
    public void Read_ShouldRejectTruncatedSampleLength()
    {
        H264AvcDecoderConfiguration configuration = new(
            66, 0, 30, 4, [], [], []);
        Mp4EncodedSample sample = new(Location(), [0, 0, 0, 5, 0x65, 0x80]);

        Assert.Throws<FormatException>(() =>
            new H264Mp4SampleNalUnitReader().Read(sample, configuration));
    }

    [Fact]
    public void Read_ShouldRejectEmptySample()
    {
        H264AvcDecoderConfiguration configuration = new(
            66, 0, 30, 2, [], [], []);
        Mp4EncodedSample sample = new(
            new Mp4SampleLocation(1, 1, 0, 0, 0, 1, true),
            []);

        Assert.Throws<FormatException>(() =>
            new H264Mp4SampleNalUnitReader().Read(sample, configuration));
    }

    [Fact]
    public void Read_ShouldRejectReservedLengthFieldSize()
    {
        H264AvcDecoderConfiguration configuration = new(
            66, 0, 30, 3, [], [], []);
        Mp4EncodedSample sample = new(Location(), [0, 1, 0x65]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new H264Mp4SampleNalUnitReader().Read(sample, configuration));
    }

    private static Mp4SampleLocation Location() =>
        new(1, 1, 0, 8, 0, 1, true);
}

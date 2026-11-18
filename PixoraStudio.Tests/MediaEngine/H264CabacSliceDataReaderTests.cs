using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacSliceDataReaderTests
{
    [Fact]
    public void Constructor_ShouldConsumeOneBitAlignmentBeforeArithmeticInitialisation()
    {
        H264BitReader bits = new(new byte[] { 0xFF, 0x00, 0x00 });
        bits.ReadFlag();

        H264CabacSliceDataReader reader = new(bits, 26, true, 0);

        Assert.Equal(17, bits.PositionBits);
        Assert.Equal(0, reader.DecodeBin(0));
    }

    [Fact]
    public void Constructor_ShouldRejectZeroCabacAlignmentBit()
    {
        H264BitReader bits = new(new byte[] { 0x80, 0x00, 0x00 });
        bits.ReadFlag();

        Assert.Throws<FormatException>(() => new H264CabacSliceDataReader(bits, 26, true, 0));
    }

    [Fact]
    public void DecodeBin_ShouldRejectContextNotDefinedForSliceType()
    {
        H264CabacSliceDataReader reader = CreateReader(isIntra: true);

        Assert.Throws<ArgumentException>(() => reader.DecodeBin(54));
        Assert.Throws<ArgumentException>(() => reader.DecodeBin(276));
    }

    [Fact]
    public void DecodeBin_ShouldRejectContextIndexOutsideRange()
    {
        H264CabacSliceDataReader reader = CreateReader(isIntra: true);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.DecodeBin(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.DecodeBin(460));
    }

    [Fact]
    public void DecodeTerminate_ShouldMarkReaderTerminatedAndRejectFurtherBins()
    {
        H264CabacSliceDataReader reader = new(
            new H264BitReader(new byte[] { 0xFE, 0x80 }), 26, true, 0);

        Assert.Equal(1, reader.DecodeTerminate());
        Assert.True(reader.IsTerminated);
        Assert.Throws<InvalidOperationException>(() => reader.DecodeBypass());
        Assert.Throws<InvalidOperationException>(() => reader.DecodeBin(0));
        Assert.Throws<InvalidOperationException>(() => reader.DecodeTerminate());
    }

    private static H264CabacSliceDataReader CreateReader(bool isIntra)
    {
        // The first nine bits after alignment are zero, so the initial offset is zero.
        return new H264CabacSliceDataReader(
            new H264BitReader(new byte[] { 0x00, 0x00 }), 26, isIntra, 0);
    }
}

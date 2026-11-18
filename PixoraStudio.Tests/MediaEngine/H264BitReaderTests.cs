using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BitReaderTests
{
    [Fact]
    public void ReadBits_ShouldReadMostSignificantBitFirst()
    {
        H264BitReader reader = new(new byte[] { 0b1011_0010 });
        Assert.Equal(5u, reader.ReadBits(3));
        Assert.Equal(0b10010u, reader.ReadBits(5));
        Assert.Equal(0, reader.RemainingBits);
    }

    [Theory]
    [InlineData(0b1000_0000, 1, 0u)]
    [InlineData(0b0100_0000, 3, 1u)]
    [InlineData(0b0110_0000, 3, 2u)]
    [InlineData(0b0010_0000, 5, 3u)]
    public void ReadUnsignedExpGolomb_ShouldDecodeValues(byte value, int validBits, uint expected)
    {
        H264BitReader reader = new(new byte[] { value }, validBits);
        Assert.Equal(expected, reader.ReadUnsignedExpGolomb());
    }

    [Theory]
    [InlineData(0b1000_0000, 1, 0)]
    [InlineData(0b0100_0000, 3, 1)]
    [InlineData(0b0110_0000, 3, -1)]
    [InlineData(0b0010_0000, 5, 2)]
    [InlineData(0b0010_1000, 5, -2)]
    public void ReadSignedExpGolomb_ShouldDecodeValues(byte value, int validBits, int expected)
    {
        H264BitReader reader = new(new byte[] { value }, validBits);
        Assert.Equal(expected, reader.ReadSignedExpGolomb());
    }

    [Fact]
    public void MoreRbspData_ShouldRecognizeStopBitAndZeroAlignmentAsTrailingBits()
    {
        H264BitReader reader = new(new byte[] { 0b1000_0000 });
        Assert.False(reader.MoreRbspData());
        reader.ConsumeRbspTrailingBits();
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void MoreRbspData_ShouldDistinguishPayloadFromTrailingBits()
    {
        H264BitReader reader = new(new byte[] { 0b1010_0000 });
        Assert.True(reader.MoreRbspData());
        Assert.Equal(0, reader.PositionBits);
    }

    [Fact]
    public void ConsumeRbspTrailingBits_ShouldRejectMissingStopBitAndNonZeroAlignment()
    {
        Assert.Throws<FormatException>(() =>
            new H264BitReader(new byte[] { 0 }).ConsumeRbspTrailingBits());
        Assert.Throws<FormatException>(() =>
            new H264BitReader(new byte[] { 0b1100_0000 }).ConsumeRbspTrailingBits());
    }

    [Fact]
    public void ReadBits_ShouldRejectTruncatedInput()
    {
        H264BitReader reader = new(new byte[] { 0x80 }, 1);
        Assert.Throws<EndOfStreamException>(() => reader.ReadBits(2));
    }

    [Fact]
    public void PeekBits_ShouldNotAdvanceReaderPosition()
    {
        H264BitReader reader = new(new byte[] { 0b1011_0010 });
        Assert.Equal(0b011u, reader.PeekBits(3, 1));
        Assert.Equal(0, reader.PositionBits);
        Assert.Equal(0b101u, reader.ReadBits(3));
    }

    [Fact]
    public void ReadUnsignedExpGolomb_ShouldRejectTruncatedCode()
    {
        H264BitReader reader = new(new byte[] { 0x00 }, 8);
        Assert.Throws<EndOfStreamException>(() => reader.ReadUnsignedExpGolomb());
    }

    [Fact]
    public void ReadBits_ShouldRejectInvalidCount()
    {
        H264BitReader reader = new(new byte[] { 0xFF });
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadBits(33));
    }
}


using PixoraStudio.MediaEngine.Decoders.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacBitReaderTests
{
    [Fact]
    public void Constructor_ShouldUseEntireBufferWhenValidLengthIsOmitted()
    {
        AacBitReader reader = new(new byte[] { 0b1010_0101 });

        Assert.Equal(8, reader.LengthBits);
        Assert.Equal(8, reader.RemainingBits);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Constructor_ShouldRejectInvalidValidBitLength(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AacBitReader(
                new byte[] { 0 },
                length));
    }

    [Fact]
    public void ReadBits_ShouldReadMostSignificantBitFirst()
    {
        AacBitReader reader = new(
            new byte[] { 0b1011_0010 });

        Assert.Equal(0b101u, reader.ReadBits(3));
        Assert.Equal(0b10010u, reader.ReadBits(5));
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void ReadBits_ShouldRejectReadsPastValidBitLength()
    {
        AacBitReader reader = new(
            new byte[] { 0b1111_1111 },
            validBitLength: 3);

        Assert.Equal(0b111u, reader.ReadBits(3));

        Assert.Throws<EndOfStreamException>(
            () => reader.ReadBits(1));
    }

    [Fact]
    public void ReadBits_ShouldNotAdvanceWhenReadIsTruncated()
    {
        AacBitReader reader = new(
            new byte[] { 0b1111_0000 },
            validBitLength: 3);

        Assert.Throws<EndOfStreamException>(
            () => reader.ReadBits(4));

        Assert.Equal(0, reader.PositionBits);
    }

    [Fact]
    public void ReadBits_ShouldAllowZeroBits()
    {
        AacBitReader reader = new(new byte[] { 0xFF });

        Assert.Equal(0u, reader.ReadBits(0));
        Assert.Equal(0, reader.PositionBits);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(33)]
    public void ReadBits_ShouldRejectInvalidBitCount(int bitCount)
    {
        AacBitReader reader = new(new byte[] { 0xFF });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.ReadBits(bitCount));
    }

    [Fact]
    public void PeekBits_ShouldNotAdvancePosition()
    {
        AacBitReader reader = new(
            new byte[] { 0b1101_0000 });

        Assert.Equal(0b110u, reader.PeekBits(3));
        Assert.Equal(0, reader.PositionBits);

        Assert.Equal(0b110u, reader.ReadBits(3));
        Assert.Equal(3, reader.PositionBits);
    }

    [Fact]
    public void ReadSignedBits_ShouldDecodePositiveValue()
    {
        AacBitReader reader = new(
            new byte[] { 0b0011_0000 });

        Assert.Equal(3, reader.ReadSignedBits(4));
    }

    [Fact]
    public void ReadSignedBits_ShouldDecodeNegativeValue()
    {
        AacBitReader reader = new(
            new byte[] { 0b1101_0000 });

        Assert.Equal(-3, reader.ReadSignedBits(4));
    }

    [Fact]
    public void ReadSignedBits_ShouldSupportThirtyTwoBits()
    {
        AacBitReader reader = new(
            new byte[] { 0xFF, 0xFF, 0xFF, 0xFE });

        Assert.Equal(-2, reader.ReadSignedBits(32));
    }

    [Fact]
    public void SkipBits_ShouldAdvancePosition()
    {
        AacBitReader reader = new(
            new byte[] { 0b1111_0000 });

        reader.SkipBits(3);

        Assert.Equal(3, reader.PositionBits);
        Assert.Equal(0b10000u, reader.ReadBits(5));
    }

    [Fact]
    public void SkipBits_ShouldRejectNegativeCount()
    {
        AacBitReader reader = new(new byte[] { 0 });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.SkipBits(-1));
    }

    [Fact]
    public void SkipBits_ShouldRejectSkippingPastEnd()
    {
        AacBitReader reader = new(
            new byte[] { 0 },
            validBitLength: 3);

        Assert.Throws<EndOfStreamException>(
            () => reader.SkipBits(4));

        Assert.Equal(0, reader.PositionBits);
    }

    [Fact]
    public void AlignToByte_ShouldSkipRemainingBitsInCurrentByte()
    {
        AacBitReader reader = new(
            new byte[] { 0b1011_0110, 0b1100_0000 });

        reader.SkipBits(3);
        reader.AlignToByte();

        Assert.True(reader.IsByteAligned);
        Assert.Equal(8, reader.PositionBits);
        Assert.Equal(0b110u, reader.ReadBits(3));
    }

    [Fact]
    public void AlignToByte_ShouldRejectAlignmentPastValidLength()
    {
        AacBitReader reader = new(
            new byte[] { 0b1010_0000 },
            validBitLength: 3);

        reader.SkipBits(1);

        Assert.Throws<EndOfStreamException>(
            () => reader.AlignToByte());

        Assert.Equal(1, reader.PositionBits);
    }

    [Fact]
    public void ReadByte_ShouldRejectUnalignedPosition()
    {
        AacBitReader reader = new(new byte[] { 0xFF });

        reader.SkipBits(1);

        Assert.Throws<InvalidOperationException>(
            () => reader.ReadByte());
    }

    [Fact]
    public void ReadByte_ShouldReadAlignedByte()
    {
        AacBitReader reader = new(
            new byte[] { 0xAB, 0xCD });

        Assert.Equal((byte)0xAB, reader.ReadByte());
        Assert.Equal((byte)0xCD, reader.ReadByte());
    }

    [Fact]
    public void ReadBytes_ShouldReadMultipleBytes()
    {
        AacBitReader reader = new(
            new byte[] { 0x12, 0x34, 0x56 });

        Assert.Equal(
            new byte[] { 0x12, 0x34 },
            reader.ReadBytes(2));

        Assert.Equal(16, reader.PositionBits);
    }

    [Fact]
    public void ReadBytes_ShouldRespectValidBitLength()
    {
        AacBitReader reader = new(
            new byte[] { 0x12, 0x34 },
            validBitLength: 12);

        Assert.Equal((byte)0x12, reader.ReadByte());

        Assert.Throws<EndOfStreamException>(
            () => reader.ReadByte());
    }

    [Fact]
    public void ReadBytes_ShouldRejectNegativeCount()
    {
        AacBitReader reader = new(new byte[] { 0 });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reader.ReadBytes(-1));
    }
}

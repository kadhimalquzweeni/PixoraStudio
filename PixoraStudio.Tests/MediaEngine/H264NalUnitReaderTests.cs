using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264NalUnitReaderTests
{
    private readonly H264NalUnitReader _reader = new();

    [Fact]
    public void ReadAnnexB_ShouldReadThreeAndFourByteStartCodes()
    {
        byte[] stream = [0, 0, 0, 1, 0x67, 0x11, 0, 0, 1, 0x68, 0x22];
        IReadOnlyList<H264NalUnit> units = _reader.ReadAnnexB(stream);
        Assert.Equal(2, units.Count);
        Assert.Equal((byte)7, units[0].NalUnitType);
        Assert.Equal((byte)8, units[1].NalUnitType);
        Assert.Equal(new byte[] { 0x11 }, units[0].Payload.ToArray());
        Assert.Equal(new byte[] { 0x22 }, units[1].Payload.ToArray());
    }

    [Fact]
    public void ReadAnnexB_ShouldTrimTrailingZeroBytes()
    {
        IReadOnlyList<H264NalUnit> units = _reader.ReadAnnexB(new byte[] { 0, 0, 1, 0x65, 0xAA, 0 });
        Assert.Equal(new byte[] { 0xAA }, units[0].Payload.ToArray());
    }

    [Fact]
    public void ReadLengthPrefixed_ShouldReadOneTwoAndFourByteLengths()
    {
        foreach (int lengthSize in new[] { 1, 2, 4 })
        {
            byte[] data = lengthSize switch
            {
                1 => [2, 0x65, 0xAA],
                2 => [0, 2, 0x65, 0xAA],
                _ => [0, 0, 0, 2, 0x65, 0xAA]
            };
            IReadOnlyList<H264NalUnit> units = _reader.ReadLengthPrefixed(data, lengthSize);
            Assert.Single(units);
            Assert.Equal((byte)5, units[0].NalUnitType);
            Assert.Equal(new byte[] { 0xAA }, units[0].Payload.ToArray());
        }
    }

    [Fact]
    public void ReadLengthPrefixed_ShouldReadMultipleUnits()
    {
        byte[] data = [2, 0x67, 0x11, 2, 0x68, 0x22];
        Assert.Equal(2, _reader.ReadLengthPrefixed(data, 1).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void ReadLengthPrefixed_ShouldRejectUnsupportedLengthSize(int lengthSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _reader.ReadLengthPrefixed(new byte[] { 1, 0x65 }, lengthSize));
    }

    [Fact]
    public void ReadLengthPrefixed_ShouldRejectTruncatedLength()
    {
        Assert.Throws<FormatException>(() => _reader.ReadLengthPrefixed(new byte[] { 0 }, 2));
    }

    [Fact]
    public void ReadLengthPrefixed_ShouldRejectZeroLength()
    {
        Assert.Throws<FormatException>(() => _reader.ReadLengthPrefixed(new byte[] { 0 }, 1));
    }

    [Fact]
    public void ReadLengthPrefixed_ShouldRejectLengthBeyondSample()
    {
        Assert.Throws<FormatException>(() => _reader.ReadLengthPrefixed(new byte[] { 3, 0x65 }, 1));
    }

    [Fact]
    public void ReadAnnexB_ShouldRejectForbiddenBit()
    {
        Assert.Throws<FormatException>(() => _reader.ReadAnnexB([0, 0, 1, 0xE5]));
    }

    [Fact]
    public void ReadAnnexB_ShouldRejectReservedNalType()
    {
        Assert.Throws<FormatException>(() => _reader.ReadAnnexB([0, 0, 1, 0x60]));
    }

    [Fact]
    public void ReadAnnexB_ShouldRejectMissingStartCode()
    {
        Assert.Throws<FormatException>(() => _reader.ReadAnnexB([0x67, 0x11]));
    }
}

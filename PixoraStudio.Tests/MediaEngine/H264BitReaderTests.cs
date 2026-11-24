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
    public void ReadSignedExpGolomb_ShouldSupportValuesNearInt32Limits()
    {
        Assert.Equal(int.MaxValue,
            new H264BitReader(EncodeUnsignedExpGolomb(uint.MaxValue - 2))
                .ReadSignedExpGolomb());
        Assert.Equal(-int.MaxValue,
            new H264BitReader(EncodeUnsignedExpGolomb(uint.MaxValue - 1))
                .ReadSignedExpGolomb());
    }

    private static byte[] EncodeUnsignedExpGolomb(uint value)
    {
        ulong code = (ulong)value + 1;
        int leadingZeroes = 0;
        for (ulong current = code; current > 1; current >>= 1)
            leadingZeroes++;

        List<bool> bits = [];
        for (int i = 0; i < leadingZeroes; i++)
            bits.Add(false);
        for (int i = leadingZeroes; i >= 0; i--)
            bits.Add(((code >> i) & 1) != 0);

        byte[] data = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i])
                data[i / 8] |= (byte)(1 << (7 - i % 8));
        return data;
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
    public void ConsumeRbspTrailingBits_ShouldIncludeBitstreamDiagnosticsWhenUnexpectedDataRemains()
    {
        H264BitReader reader = new(new byte[] { 0b1000_0000, 0b1100_0000 }, 10);

        FormatException exception = Assert.Throws<FormatException>(
            () => reader.ConsumeRbspTrailingBits());

        Assert.Contains("Bit position:", exception.Message);
        Assert.Contains("total RBSP bits: 10", exception.Message);
        Assert.Contains("remaining bits:", exception.Message);
        Assert.Contains("remaining bit sequence: 11", exception.Message);
        Assert.Contains("RBSP hex: 80C0", exception.Message);
    }

    [Fact]
    public void ConsumeRbspTrailingBitsAndZeroPadding_ShouldAcceptCompleteZeroBytesAfterTrailingBits()
    {
        H264BitReader reader = new(new byte[] { 0b1000_0000, 0x00 });

        reader.ConsumeRbspTrailingBitsAndZeroPadding();

        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void ConsumeRbspTrailingBitsAndZeroPadding_ShouldRejectNonZeroPadding()
    {
        H264BitReader reader = new(new byte[] { 0b1000_0000, 0x01 });

        FormatException exception = Assert.Throws<FormatException>(
            () => reader.ConsumeRbspTrailingBitsAndZeroPadding());

        Assert.Contains("Trailing SPS padding bytes must be zero", exception.Message);
    }

    [Fact]
    public void ReadFlag_ShouldReadBitsMostSignificantBitFirstAcrossByteBoundaries()
    {
        H264BitReader reader = new(new byte[] { 0b1010_0101, 0b1100_0011 });

        bool[] actual = Enumerable.Range(0, 16)
            .Select(_ => reader.ReadFlag())
            .ToArray();

        Assert.Equal(
            new[] { true, false, true, false, false, true, false, true,
                    true, true, false, false, false, false, true, true },
            actual);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void ReadFlag_ShouldThrowAtEndWithoutAdvancingPosition()
    {
        H264BitReader reader = new(new byte[] { 0x80 }, 1);

        Assert.True(reader.ReadFlag());
        Assert.Equal(1, reader.PositionBits);
        Assert.Throws<EndOfStreamException>(() => reader.ReadFlag());
        Assert.Equal(1, reader.PositionBits);
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

    [Theory]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x80, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00 })]
    public void ConsumeCabacTrailingBits_ShouldAcceptStopBitAndZeroWords(byte[] data)
    {
        H264BitReader reader = new(data);

        reader.ConsumeCabacTrailingBits();

        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void ConsumeCabacTrailingBits_ShouldRejectPartialZeroWord()
    {
        H264BitReader reader = new(new byte[] { 0x80, 0x00 });

        Assert.Throws<FormatException>(() => reader.ConsumeCabacTrailingBits());
    }

    [Fact]
    public void ConsumeCabacTrailingBits_ShouldRejectNonZeroPaddingWord()
    {
        H264BitReader reader = new(new byte[] { 0x80, 0x00, 0x01 });

        Assert.Throws<FormatException>(() => reader.ConsumeCabacTrailingBits());
    }

    [Fact]
    public void ConsumeCabacTrailingBits_ShouldRejectMissingStopBit()
    {
        H264BitReader reader = new(new byte[] { 0x00 });

        Assert.Throws<FormatException>(() => reader.ConsumeCabacTrailingBits());
    }


    [Fact]
    public void ReadBitsAndPeekBits_ShouldMatchReferenceAcrossAlignmentsAndWidths()
    {
        byte[] data = new byte[16];
        new Random(1729).NextBytes(data);

        for (int start = 0; start <= 95; start++)
        {
            for (int count = 0; count <= 32 && start + count <= data.Length * 8; count++)
            {
                H264BitReader reader = new(data);
                reader.SkipBits(start);

                uint expected = 0;
                for (int bit = 0; bit < count; bit++)
                {
                    int position = start + bit;
                    uint next = (uint)((data[position >> 3] >> (7 - (position & 7))) & 1);
                    expected = (expected << 1) | next;
                }

                Assert.Equal(expected, reader.PeekBits(count));
                Assert.Equal(start, reader.PositionBits);
                Assert.Equal(expected, reader.ReadBits(count));
                Assert.Equal(start + count, reader.PositionBits);
            }
        }
    }


    [Fact]
    public void ReadUnsignedExpGolomb_ShouldRoundTripValuesAcrossPrefixLengths()
    {
        for (uint value = 0; value <= 4095; value++)
        {
            byte[] encoded = EncodeUnsignedExpGolomb(value);
            H264BitReader reader = new(encoded);

            Assert.Equal(value, reader.ReadUnsignedExpGolomb());
        }
    }

    [Fact]
    public void ReadUnsignedExpGolomb_ShouldRejectThirtyTwoLeadingZeroBits()
    {
        H264BitReader reader = new(new byte[4]);

        Assert.Throws<FormatException>(() => reader.ReadUnsignedExpGolomb());
    }

}

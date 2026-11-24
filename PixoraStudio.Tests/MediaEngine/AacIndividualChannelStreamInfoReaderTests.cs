
using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacIndividualChannelStreamInfoReaderTests
{
    private readonly AacIndividualChannelStreamInfoReader _reader = new();

    [Theory]
    [InlineData(0, AacWindowSequence.OnlyLongSequence)]
    [InlineData(1, AacWindowSequence.LongStartSequence)]
    [InlineData(2, AacWindowSequence.EightShortSequence)]
    [InlineData(3, AacWindowSequence.LongStopSequence)]
    public void Read_ShouldParseWindowSequence(
        int sequence,
        AacWindowSequence expected)
    {
        AacBitReader reader = CreateLongWindowStream(
            sequence,
            windowShape: 0,
            maxScaleFactorBands: 32,
            predictorDataPresent: 0);

        AacIndividualChannelStreamInfo result =
            _reader.Read(reader);

        Assert.Equal(expected, result.WindowSequence);
    }

    [Fact]
    public void Read_ShouldParseLongWindowInformation()
    {
        AacBitReader reader = CreateLongWindowStream(
            sequence: 0,
            windowShape: 1,
            maxScaleFactorBands: 42,
            predictorDataPresent: 0);

        AacIndividualChannelStreamInfo result =
            _reader.Read(reader);

        Assert.Equal(
            AacWindowSequence.OnlyLongSequence,
            result.WindowSequence);

        Assert.Equal(
            AacWindowShape.KaiserBesselDerived,
            result.WindowShape);

        Assert.Equal((byte)42, result.MaxScaleFactorBands);
        Assert.Equal((byte)0, result.ScaleFactorGrouping);
        Assert.False(result.PredictorDataPresent);
        Assert.False(result.UsesShortWindows);
        Assert.Equal(1, result.WindowGroupCount);
    }

    [Fact]
    public void Read_ShouldParseShortWindowInformation()
    {
        AacBitReader reader = CreateShortWindowStream(
            windowShape: 0,
            maxScaleFactorBands: 12,
            grouping: 0b111_1111);

        AacIndividualChannelStreamInfo result =
            _reader.Read(reader);

        Assert.Equal(
            AacWindowSequence.EightShortSequence,
            result.WindowSequence);

        Assert.Equal(AacWindowShape.Sine, result.WindowShape);
        Assert.Equal((byte)12, result.MaxScaleFactorBands);
        Assert.Equal((byte)0b111_1111, result.ScaleFactorGrouping);
        Assert.False(result.PredictorDataPresent);
        Assert.True(result.UsesShortWindows);
        Assert.Equal(1, result.WindowGroupCount);
    }

    [Theory]
    [InlineData(0b000_0000, 8)]
    [InlineData(0b111_1111, 1)]
    [InlineData(0b101_0101, 4)]
    [InlineData(0b010_1010, 5)]
    public void Read_ShouldCalculateShortWindowGroupCount(
        int grouping,
        int expectedGroupCount)
    {
        AacBitReader reader = CreateShortWindowStream(
            windowShape: 0,
            maxScaleFactorBands: 10,
            grouping: grouping);

        AacIndividualChannelStreamInfo result =
            _reader.Read(reader);

        Assert.Equal(expectedGroupCount, result.WindowGroupCount);
    }


    [Theory]
    [InlineData(0b111_1111, "8")]
    [InlineData(0b000_0000, "1,1,1,1,1,1,1,1")]
    [InlineData(0b101_0101, "2,2,2,2")]
    [InlineData(0b010_1010, "1,2,2,2,1")]
    public void Read_ShouldExposeShortWindowGroupLengths(
        int grouping,
        string expectedLengths)
    {
        var result = _reader.Read(CreateShortWindowStream(
            windowShape: 0,
            maxScaleFactorBands: 10,
            grouping: grouping));

        int[] expected = expectedLengths.Split(',').Select(int.Parse).ToArray();
        Assert.Equal(expected, result.WindowGroupLengths);
        Assert.Equal(expected.Length, result.WindowGroupCount);
    }

    [Fact]
    public void Read_ShouldRejectNonZeroReservedBit()
    {
        BitWriter writer = new();

        writer.Write(1, 1);
        writer.Write(0, 2);
        writer.Write(0, 1);
        writer.Write(32, 6);
        writer.Write(0, 1);

        AacBitReader reader = new(writer.ToArray());

        Assert.Throws<InvalidDataException>(
            () => _reader.Read(reader));
    }

    [Fact]
    public void Read_ShouldRejectLongWindowPredictionData()
    {
        AacBitReader reader = CreateLongWindowStream(
            sequence: 0,
            windowShape: 0,
            maxScaleFactorBands: 32,
            predictorDataPresent: 1);

        Assert.Throws<NotSupportedException>(
            () => _reader.Read(reader));
    }

    [Fact]
    public void Read_ShouldRejectTruncatedLongWindowInformation()
    {
        // Reserved bit, sequence and shape are present,
        // but max_sfb and predictor_data_present are incomplete.
        AacBitReader reader = new(new byte[] { 0b0000_0000 });

        Assert.Throws<EndOfStreamException>(
            () => _reader.Read(reader));
    }

    [Fact]
    public void Read_ShouldRejectTruncatedShortWindowInformation()
    {
        // Only the first part of the short-window syntax is present.
        BitWriter writer = new();

        writer.Write(0, 1);
        writer.Write(2, 2);
        writer.Write(0, 1);
        writer.Write(10, 4);

        AacBitReader reader = new(writer.ToArray());

        Assert.Throws<EndOfStreamException>(
            () => _reader.Read(reader));
    }

    [Fact]
    public void Read_ShouldRejectNullReader()
    {
        Assert.Throws<ArgumentNullException>(
            () => _reader.Read(null!));
    }

    [Fact]
    public void Read_ShouldLeaveFollowingBitsAvailable()
    {
        BitWriter writer = new();

        writer.Write(0, 1);
        writer.Write(0, 2);
        writer.Write(0, 1);
        writer.Write(32, 6);
        writer.Write(0, 1);

        writer.Write(0b101, 3);

        AacBitReader reader = new(writer.ToArray());

        _reader.Read(reader);

        Assert.Equal(0b101u, reader.ReadBits(3));
    }

    private static AacBitReader CreateLongWindowStream(
        int sequence,
        int windowShape,
        int maxScaleFactorBands,
        int predictorDataPresent)
    {
        BitWriter writer = new();

        writer.Write(0, 1);
        writer.Write(sequence, 2);
        writer.Write(windowShape, 1);
        writer.Write(maxScaleFactorBands, 6);
        writer.Write(predictorDataPresent, 1);

        return new AacBitReader(writer.ToArray());
    }

    private static AacBitReader CreateShortWindowStream(
        int windowShape,
        int maxScaleFactorBands,
        int grouping)
    {
        BitWriter writer = new();

        writer.Write(0, 1);
        writer.Write(2, 2);
        writer.Write(windowShape, 1);
        writer.Write(maxScaleFactorBands, 4);
        writer.Write(grouping, 7);

        return new AacBitReader(writer.ToArray());
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _position;

        public void Write(int value, int bitCount)
        {
            if (bitCount is < 0 or > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(bitCount));
            }

            if (bitCount < 32 &&
                (uint)value >= (1u << bitCount))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            for (int bit = bitCount - 1; bit >= 0; bit--)
            {
                if (_position == 0)
                {
                    _bytes.Add(0);
                }

                int bitValue = (value >> bit) & 1;

                if (bitValue != 0)
                {
                    int byteIndex = _bytes.Count - 1;
                    _bytes[byteIndex] |= (byte)(1 << (7 - _position));
                }

                _position = (_position + 1) & 7;
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}

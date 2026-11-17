using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacTnsDataReaderTests
{
    private readonly AacTnsDataReader _reader = new();

    [Fact]
    public void Read_ShouldParseLongWindowFilterAndSignedCoefficients()
    {
        var bits = new BitWriter();
        bits.Write(1, 2); // one filter
        bits.Write(1, 1); // coefficient resolution
        bits.Write(12, 6);
        bits.Write(3, 5);
        bits.Write(1, 1); // direction
        bits.Write(0, 1); // not compressed
        bits.Write(0b1110, 4); // -2
        bits.Write(0b0011, 4); // +3
        bits.Write(0b1000, 4); // -8

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length), LongWindowInfo());

        var window = Assert.Single(result.Windows);
        Assert.True(window.CoefficientResolution);
        var filter = Assert.Single(window.Filters);
        Assert.Equal(12, filter.Length);
        Assert.Equal(3, filter.Order);
        Assert.True(filter.IsDirectionReversed);
        Assert.False(filter.CoefficientsAreCompressed);
        Assert.Equal(new[] { -2, 3, -8 }, filter.Coefficients);
    }

    [Fact]
    public void Read_ShouldParseEightShortWindows()
    {
        var bits = new BitWriter();
        for (int i = 0; i < 8; i++)
        {
            bits.Write(i == 2 ? 1 : 0, 1);
            if (i == 2)
            {
                bits.Write(1, 1);
                bits.Write(7, 4);
                bits.Write(2, 3);
                bits.Write(0, 1);
                bits.Write(0, 1);
                bits.Write(0b1101, 4); // -3 in four-bit two's complement
                bits.Write(0b0011, 4); // +3 in four-bit two's complement
            }
        }

        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence, AacWindowShape.Sine, 10, 0, false);
        var result = _reader.Read(new AacBitReader(bits.ToArray(), bits.Length), info);

        Assert.Equal(8, result.Windows.Count);
        Assert.Empty(result.Windows[0].Filters);
        Assert.Equal(1, result.Windows[2].Filters.Count);
        Assert.Equal(new[] { -3, 3 }, result.Windows[2].Filters[0].Coefficients);
    }

    [Fact]
    public void Read_ShouldAllowZeroOrderFilterWithoutCoefficientSyntax()
    {
        var bits = new BitWriter();
        bits.Write(1, 2);
        bits.Write(0, 1);
        bits.Write(10, 6);
        bits.Write(0, 5);

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length), LongWindowInfo());

        var filter = Assert.Single(Assert.Single(result.Windows).Filters);
        Assert.Equal(0, filter.Order);
        Assert.Empty(filter.Coefficients);
    }

    [Fact]
    public void Read_ShouldRejectOrderAboveLongWindowLimit()
    {
        var bits = new BitWriter();
        bits.Write(1, 2);
        bits.Write(0, 1);
        bits.Write(0, 6);
        bits.Write(13, 5);

        Assert.Throws<InvalidDataException>(() =>
            _reader.Read(new AacBitReader(bits.ToArray(), bits.Length), LongWindowInfo()));
    }

    [Fact]
    public void Read_ShouldRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => _reader.Read(null!, LongWindowInfo()));
        Assert.Throws<ArgumentNullException>(() => _reader.Read(new AacBitReader(Array.Empty<byte>()), null!));
    }

    private static AacIndividualChannelStreamInfo LongWindowInfo() =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, 20, 0, false);

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        public int Length { get; private set; }
        public void Write(int value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--)
            {
                if ((Length & 7) == 0) _bytes.Add(0);
                if (((value >> bit) & 1) != 0)
                    _bytes[^1] |= (byte)(1 << (7 - (Length & 7)));
                Length++;
            }
        }
        public byte[] ToArray() => _bytes.ToArray();
    }
}

using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacSectionDataReaderTests
{
    private readonly AacSectionDataReader _reader = new();

    [Fact]
    public void Read_ShouldReadLongWindowSections()
    {
        // max_sfb = 3; sections: codebook 1 length 2, codebook 0 length 1.
        var bits = new BitWriter();
        bits.Write(1, 4); bits.Write(2, 5);
        bits.Write(0, 4); bits.Write(1, 5);

        var info = LongWindow(maxSfb: 3);
        var result = _reader.Read(new AacBitReader(bits.ToArray(), bits.Length), info);

        Assert.Collection(result,
            section =>
            {
                Assert.Equal(0, section.WindowGroup);
                Assert.Equal(0, section.StartScaleFactorBand);
                Assert.Equal(2, section.ScaleFactorBandCount);
                Assert.Equal(1, section.CodebookIndex);
            },
            section =>
            {
                Assert.Equal(2, section.StartScaleFactorBand);
                Assert.Equal(1, section.ScaleFactorBandCount);
                Assert.Equal(0, section.CodebookIndex);
            });
    }

    [Fact]
    public void Read_ShouldAccumulateEscapedLongSectionLengths()
    {
        // 31 + 31 + 2 = 64 bands.
        var bits = new BitWriter();
        bits.Write(1, 4);
        bits.Write(31, 5);
        bits.Write(31, 5);
        bits.Write(2, 5);

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length),
            LongWindow(maxSfb: 64));

        var section = Assert.Single(result);
        Assert.Equal(64, section.ScaleFactorBandCount);
    }

    [Fact]
    public void Read_ShouldReadShortWindowGroups()
    {
        // Grouping 1010101 gives four groups. Each group has one section.
        var bits = new BitWriter();
        for (int i = 0; i < 4; i++)
        {
            bits.Write(i, 4);
            bits.Write(2, 3);
        }

        var info = new AacIndividualChannelStreamInfo(
            AacWindowSequence.EightShortSequence,
            AacWindowShape.Sine,
            2,
            0b1010101,
            false);

        var result = _reader.Read(new AacBitReader(bits.ToArray(), bits.Length), info);

        Assert.Equal(4, result.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, result.Select(x => x.WindowGroup));
        Assert.All(result, x => Assert.Equal(2, x.ScaleFactorBandCount));
    }

    [Fact]
    public void Read_ShouldReturnEmptyWhenMaxSfbIsZero()
    {
        var result = _reader.Read(
            new AacBitReader(Array.Empty<byte>()),
            LongWindow(maxSfb: 0));

        Assert.Empty(result);
    }

    [Fact]
    public void Read_ShouldRejectSectionThatExceedsRemainingBands()
    {
        var bits = new BitWriter();
        bits.Write(1, 4);
        bits.Write(4, 5);

        Assert.Throws<InvalidDataException>(() =>
            _reader.Read(
                new AacBitReader(bits.ToArray(), bits.Length),
                LongWindow(maxSfb: 3)));
    }

    [Fact]
    public void Read_ShouldRejectZeroLengthSection()
    {
        var bits = new BitWriter();
        bits.Write(1, 4);
        bits.Write(0, 5);

        Assert.Throws<InvalidDataException>(() =>
            _reader.Read(
                new AacBitReader(bits.ToArray(), bits.Length),
                LongWindow(maxSfb: 1)));
    }

    [Fact]
    public void Read_ShouldRejectNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _reader.Read(null!, LongWindow(1)));
        Assert.Throws<ArgumentNullException>(() =>
            _reader.Read(new AacBitReader(Array.Empty<byte>()), null!));
    }

    private static AacIndividualChannelStreamInfo LongWindow(byte maxSfb) =>
        new(AacWindowSequence.OnlyLongSequence, AacWindowShape.Sine, maxSfb, 0, false);

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        public int Length { get; private set; }

        public void Write(int value, int count)
        {
            for (int bit = count - 1; bit >= 0; bit--)
            {
                if ((Length & 7) == 0)
                    _bytes.Add(0);

                if (((value >> bit) & 1) != 0)
                    _bytes[^1] |= (byte)(1 << (7 - (Length & 7)));

                Length++;
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}

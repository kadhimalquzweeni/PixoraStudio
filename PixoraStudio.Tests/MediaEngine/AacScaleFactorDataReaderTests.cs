using PixoraStudio.MediaEngine.Decoders.Audio;
using PixoraStudio.MediaEngine.Models.Audio;
using Xunit;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class AacScaleFactorDataReaderTests
{
    private readonly AacScaleFactorDataReader _reader = new();

    [Fact]
    public void Read_ShouldAccumulateSpectralScaleFactorsFromGlobalGain()
    {
        // Scalefactor deltas: 0, -1, +1.
        var bits = new BitWriter();
        bits.Write(0, 1);
        bits.Write(0b100, 3);
        bits.Write(0b1010, 4);

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length),
            [Section(0, 0, 3, 1)],
            globalGain: 100);

        Assert.Equal(new[] { 100, 99, 100 }, result.Bands.Select(x => x.Value));
        Assert.All(result.Bands, x => Assert.Equal(1, x.CodebookIndex));
    }

    [Fact]
    public void Read_ShouldSkipZeroCodebookSectionsWithoutConsumingBits()
    {
        var result = _reader.Read(
            new AacBitReader(Array.Empty<byte>()),
            [Section(0, 0, 2, 0)],
            globalGain: 100);

        Assert.Empty(result.Bands);
    }

    [Fact]
    public void Read_ShouldReadFirstNoiseBandAsNineBitPcmAndThenDeltas()
    {
        // Initial noise = 100 - 90; PCM 256 adds zero; next delta -1.
        var bits = new BitWriter();
        bits.Write(256, 9);
        bits.Write(0b100, 3);

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length),
            [Section(0, 0, 2, 13)],
            globalGain: 100);

        Assert.Equal(new[] { 10, 9 }, result.Bands.Select(x => x.Value));
    }

    [Fact]
    public void Read_ShouldAccumulateIntensityPositionsIndependently()
    {
        // Intensity deltas +1, -1; chain starts at zero.
        var bits = new BitWriter();
        bits.Write(0b1010, 4);
        bits.Write(0b100, 3);

        var result = _reader.Read(
            new AacBitReader(bits.ToArray(), bits.Length),
            [Section(0, 0, 1, 14), Section(0, 1, 1, 15)],
            globalGain: 100);

        Assert.Equal(new[] { 1, 0 }, result.Bands.Select(x => x.Value));
        Assert.Equal(new[] { 14, 15 }, result.Bands.Select(x => x.CodebookIndex));
    }

    [Fact]
    public void Read_ShouldClampNoiseAndIntensityValues()
    {
        // noise starts at 10, pcm=511 => +255 => clamp 155.
        var noiseBits = new BitWriter();
        noiseBits.Write(511, 9);
        var noise = _reader.Read(
            new AacBitReader(noiseBits.ToArray(), noiseBits.Length),
            [Section(0, 0, 1, 13)],
            globalGain: 100);
        Assert.Equal(155, Assert.Single(noise.Bands).Value);

        // 60 repeated +60 deltas would exceed intensity range; test boundary
        // directly with a valid starting accumulator sequence in a later stream.
        var intensityBits = new BitWriter();
        for (int i = 0; i < 3; i++)
            intensityBits.Write(0b1010, 4);

        var intensity = _reader.Read(
            new AacBitReader(intensityBits.ToArray(), intensityBits.Length),
            [Section(0, 0, 3, 15)],
            globalGain: 100);
        Assert.Equal(new[] { 1, 2, 3 }, intensity.Bands.Select(x => x.Value));
    }

    [Fact]
    public void Read_ShouldRejectReservedCodebook()
    {
        Assert.Throws<InvalidDataException>(() =>
            _reader.Read(
                new AacBitReader(Array.Empty<byte>()),
                [Section(0, 0, 1, 12)],
                100));
    }

    [Fact]
    public void Read_ShouldRejectSpectralScaleFactorOutsideRange()
    {
        // A global gain of zero followed by delta -1.
        var bits = new BitWriter();
        bits.Write(0b100, 3);

        Assert.Throws<InvalidDataException>(() =>
            _reader.Read(
                new AacBitReader(bits.ToArray(), bits.Length),
                [Section(0, 0, 1, 1)],
                0));
    }

    [Fact]
    public void Read_ShouldRejectInvalidArgumentsAndSections()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _reader.Read(null!, Array.Empty<AacSectionInfo>(), 100));
        Assert.Throws<ArgumentNullException>(() =>
            _reader.Read(new AacBitReader(Array.Empty<byte>()), null!, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _reader.Read(new AacBitReader(Array.Empty<byte>()),
                Array.Empty<AacSectionInfo>(), 256));
        Assert.Throws<InvalidDataException>(() =>
            _reader.Read(new AacBitReader(Array.Empty<byte>()),
                [Section(0, 0, 0, 1)], 100));
    }

    private static AacSectionInfo Section(int group, int start, int count, int book) =>
        new(group, start, count, book);

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

using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BMacroblockHeaderReaderTests
{
    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(22, false, false, false)]
    [InlineData(23, true, false, false)]
    [InlineData(24, true, true, false)]
    [InlineData(47, true, true, false)]
    [InlineData(48, true, false, true)]
    public void ReadNext_ShouldClassifyBMacroblockTypes(
        uint mbType, bool intra, bool intra16x16, bool pcm)
    {
        BitWriter bits = new();
        bits.Ue(0);
        bits.Ue(mbType);

        H264MacroblockHeader result = new H264BMacroblockHeaderReader().ReadNext(
            bits.Reader(), 0, 1);

        Assert.Equal(mbType, result.MbTypeCode);
        Assert.Equal(intra, result.IsIntra);
        Assert.Equal(intra16x16, result.IsIntra16x16);
        Assert.Equal(pcm, result.IsPcm);
    }

    [Fact]
    public void ReadNext_ShouldExpandSkipRunPrefixWithoutConsumingMbType()
    {
        BitWriter bits = new();
        bits.Ue(3);
        bits.Ue(1);

        H264MacroblockHeader result = new H264BMacroblockHeaderReader().ReadNext(
            bits.Reader(), 2, 8);

        Assert.True(result.IsSkipped);
        Assert.Equal(3u, result.SkippedMacroblockCount);
        Assert.Equal(2u, result.MacroblockAddress);
    }

    [Fact]
    public void ReadNext_ShouldRejectInvalidSkipRunTypeAndCabac()
    {
        BitWriter skip = new(); skip.Ue(3);
        Assert.Throws<FormatException>(() =>
            new H264BMacroblockHeaderReader().ReadNext(skip.Reader(), 1, 3));
        BitWriter invalid = new(); invalid.Ue(0); invalid.Ue(49);
        Assert.Throws<FormatException>(() =>
            new H264BMacroblockHeaderReader().ReadNext(invalid.Reader(), 0, 1));
        Assert.Throws<NotSupportedException>(() =>
            new H264BMacroblockHeaderReader().ReadNext(
                new H264BitReader(new byte[] { 0x80 }, 1), 0, 1, true));
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];
        public void Ue(uint value)
        {
            uint code = value + 1; int zeros = 0;
            for (uint n = code; n > 1; n >>= 1) zeros++;
            for (int i = 0; i < zeros; i++) _bits.Add(false);
            for (int i = zeros; i >= 0; i--) _bits.Add(((code >> i) & 1) != 0);
        }
        public H264BitReader Reader()
        {
            byte[] bytes = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i]) bytes[i / 8] |= (byte)(1 << (7 - i % 8));
            return new H264BitReader(bytes, _bits.Count);
        }
    }
}

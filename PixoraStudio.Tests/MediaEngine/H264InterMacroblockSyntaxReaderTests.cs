using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterMacroblockSyntaxReaderTests
{
    [Fact]
    public void Read_P16x16_ShouldReadReferenceIndexAndMotionVectorDifference()
    {
        BitWriter bits = new();
        bits.WriteUe(1); // ref_idx_l0
        bits.WriteSe(-2);
        bits.WriteSe(3);

        H264InterMacroblockSyntax result = Read(bits, 0, 1);

        Assert.Single(result.Partitions);
        Assert.Equal((uint)1, result.Partitions[0].ReferenceIndex);
        Assert.Equal((16, 16), (result.Partitions[0].Width, result.Partitions[0].Height));
        Assert.Equal(new H264MotionVectorDifference(-2, 3),
            Assert.Single(Assert.Single(result.Partitions[0].SubPartitions).MotionVectorDifferences));
    }

    [Fact]
    public void Read_P16x8_ShouldReadBothReferenceIndicesBeforeMotionVectors()
    {
        BitWriter bits = new();
        bits.WriteUe(1);
        bits.WriteUe(0);
        bits.WriteSe(1); bits.WriteSe(2);
        bits.WriteSe(-3); bits.WriteSe(4);

        H264InterMacroblockSyntax result = Read(bits, 1, 1);

        Assert.Equal(2, result.Partitions.Count);
        Assert.Equal(new uint[] { 1, 0 }, result.Partitions.Select(p => p.ReferenceIndex));
        Assert.Equal((0, 0, 16, 8),
            (result.Partitions[0].X, result.Partitions[0].Y, result.Partitions[0].Width, result.Partitions[0].Height));
        Assert.Equal((0, 8, 16, 8),
            (result.Partitions[1].X, result.Partitions[1].Y, result.Partitions[1].Width, result.Partitions[1].Height));
        Assert.Equal(new H264MotionVectorDifference(-3, 4),
            Assert.Single(Assert.Single(result.Partitions[1].SubPartitions).MotionVectorDifferences));
    }

    [Fact]
    public void Read_P8x16_ShouldCreateLeftAndRightPartitions()
    {
        BitWriter bits = new();
        bits.WriteUe(0); bits.WriteUe(1);
        bits.WriteSe(5); bits.WriteSe(-6);
        bits.WriteSe(7); bits.WriteSe(-8);

        H264InterMacroblockSyntax result = Read(bits, 2, 1);

        Assert.Equal((0, 0, 8, 16),
            (result.Partitions[0].X, result.Partitions[0].Y, result.Partitions[0].Width, result.Partitions[0].Height));
        Assert.Equal((8, 0, 8, 16),
            (result.Partitions[1].X, result.Partitions[1].Y, result.Partitions[1].Width, result.Partitions[1].Height));
        Assert.Equal(new H264MotionVectorDifference(7, -8),
            Assert.Single(Assert.Single(result.Partitions[1].SubPartitions).MotionVectorDifferences));
    }

    [Fact]
    public void Read_P8x8_ShouldParseAllFourSubMacroblockShapesAndMvdValues()
    {
        BitWriter bits = new();
        bits.WriteUe(0); bits.WriteUe(1); bits.WriteUe(2); bits.WriteUe(3);
        bits.WriteUe(0); bits.WriteUe(1); bits.WriteUe(0); bits.WriteUe(1);
        for (int i = 0; i < 9; i++)
        {
            bits.WriteSe(i - 4);
            bits.WriteSe(4 - i);
        }

        H264InterMacroblockSyntax result = Read(bits, 3, 1);

        Assert.Equal(4, result.Partitions.Count);
        Assert.Equal(new[] { 1, 2, 2, 4 }, result.Partitions.Select(p => p.SubPartitions.Count));
        Assert.Equal(new uint[] { 0, 1, 0, 1 }, result.Partitions.Select(p => p.ReferenceIndex));
        Assert.Equal((0, 0, 8, 8),
            (result.Partitions[0].SubPartitions[0].X, result.Partitions[0].SubPartitions[0].Y,
             result.Partitions[0].SubPartitions[0].Width, result.Partitions[0].SubPartitions[0].Height));
        Assert.Equal((8, 0, 8, 4),
            (result.Partitions[1].SubPartitions[0].X, result.Partitions[1].SubPartitions[0].Y,
             result.Partitions[1].SubPartitions[0].Width, result.Partitions[1].SubPartitions[0].Height));
        Assert.Equal((4, 8, 4, 8),
            (result.Partitions[2].SubPartitions[1].X, result.Partitions[2].SubPartitions[1].Y,
             result.Partitions[2].SubPartitions[1].Width, result.Partitions[2].SubPartitions[1].Height));
        Assert.Equal((12, 12, 4, 4),
            (result.Partitions[3].SubPartitions[3].X, result.Partitions[3].SubPartitions[3].Y,
             result.Partitions[3].SubPartitions[3].Width, result.Partitions[3].SubPartitions[3].Height));
    }

    [Fact]
    public void Read_P8x8Ref0_ShouldNotConsumeReferenceIndexSyntax()
    {
        BitWriter bits = new();
        for (int i = 0; i < 4; i++) bits.WriteUe(0);
        for (int i = 0; i < 4; i++) { bits.WriteSe(i); bits.WriteSe(-i); }

        H264BitReader reader = bits.CreateReader();
        H264InterMacroblockSyntax result = new H264InterMacroblockSyntaxReader().Read(
            reader, Header(4), 3);

        Assert.True(result.ReferenceIndexForcedToZero);
        Assert.All(result.Partitions, p => Assert.Equal((uint)0, p.ReferenceIndex));
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void Read_ShouldRejectReferenceIndexOutsideActiveList()
    {
        BitWriter bits = new();
        bits.WriteUe(2);
        Assert.Throws<FormatException>(() => Read(bits, 0, 1));
    }

    [Fact]
    public void Read_ShouldRejectInvalidSubMacroblockType()
    {
        BitWriter bits = new();
        bits.WriteUe(4);
        Assert.Throws<FormatException>(() => Read(bits, 3, 0));
    }

    [Fact]
    public void Read_ShouldRejectIntraSkippedAndUnsupportedMacroblockTypes()
    {
        H264InterMacroblockSyntaxReader reader = new();
        Assert.Throws<ArgumentException>(() => reader.Read(new H264BitReader(new byte[1]),
            Header(0, intra: true), 0));
        Assert.Throws<ArgumentException>(() => reader.Read(new H264BitReader(new byte[1]),
            Header(0, skipped: true), 0));
        Assert.Throws<NotSupportedException>(() => reader.Read(new H264BitReader(new byte[1]),
            Header(5), 0));
    }

    [Fact]
    public void Read_ShouldRejectReferenceCountAboveSyntaxLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Read(new BitWriter(), 0, 32));
    }

    private static H264InterMacroblockSyntax Read(BitWriter bits, uint type, uint refs) =>
        new H264InterMacroblockSyntaxReader().Read(bits.CreateReader(), Header(type), refs);

    private static H264MacroblockHeader Header(uint type, bool intra = false, bool skipped = false) =>
        new(type, type, skipped, skipped ? 1u : 0u, intra, false, null, null, null, false);

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];
        public int RemainingBits => _bits.Count;
        public void WriteUe(uint value)
        {
            uint code = value + 1;
            int zeros = 0;
            for (uint n = code; n > 1; n >>= 1) zeros++;
            for (int i = 0; i < zeros; i++) _bits.Add(false);
            for (int i = zeros; i >= 0; i--) _bits.Add(((code >> i) & 1) != 0);
        }
        public void WriteSe(int value) => WriteUe(value <= 0
            ? checked((uint)(-2L * value))
            : checked((uint)(2L * value - 1)));
        public H264BitReader CreateReader()
        {
            byte[] bytes = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i]) bytes[i / 8] |= (byte)(1 << (7 - i % 8));
            return new H264BitReader(bytes, _bits.Count);
        }
    }
}

using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterMacroblockDecoderTests
{
    private readonly H264InterMacroblockDecoder _decoder = new();

    [Fact]
    public void Decode_P16x16_ShouldParseReferenceIndexAndMvdThenReconstructVector()
    {
        BitWriter bits = new();
        bits.WriteUe(1); // ref_idx_l0
        bits.WriteSe(2); // mvd_l0[0][0]
        bits.WriteSe(-1); // mvd_l0[0][1]
        H264MacroblockHeader header = CreateInterHeader(0);
        H264MotionVectorPictureState state = new(1, 1);

        H264InterMacroblockDecodeResult result = _decoder.Decode(
            new H264BitReader(bits.ToArray()), header, 1, state, 0, 0, 3);

        Assert.Equal((uint)0, result.Syntax.MacroblockType);
        H264ReconstructedInterPartition partition = Assert.Single(result.ReconstructedPartitions);
        Assert.Equal(1, partition.ReferenceIndex);
        Assert.Equal(new H264MotionVector(2, -1), partition.MotionVector);
    }

    [Fact]
    public void Decode_P8x8ref0_ShouldParseSubTypesWithoutReferenceIndexSyntax()
    {
        BitWriter bits = new();
        for (int i = 0; i < 4; i++)
            bits.WriteUe(0); // four P_L0_8x8 sub-macroblock types
        for (int i = 0; i < 4; i++)
        {
            bits.WriteSe(i + 1);
            bits.WriteSe(-(i + 1));
        }

        H264InterMacroblockDecodeResult result = _decoder.Decode(
            new H264BitReader(bits.ToArray()), CreateInterHeader(4),
            3, new H264MotionVectorPictureState(1, 1), 0, 0, 2);

        Assert.True(result.Syntax.ReferenceIndexForcedToZero);
        Assert.Equal(4, result.Syntax.Partitions.Count);
        Assert.Equal(4, result.ReconstructedPartitions.Count);
        Assert.All(result.ReconstructedPartitions, partition =>
            Assert.Equal(0, partition.ReferenceIndex));
        Assert.Equal(new H264MotionVector(1, -1), result.ReconstructedPartitions[0].MotionVector);
    }

    [Fact]
    public void Decode_ShouldRejectReferenceIndexOutsideActiveList()
    {
        BitWriter bits = new();
        bits.WriteUe(2); // ref_idx_l0 is outside a two-entry list (0..1)
        Assert.Throws<FormatException>(() => _decoder.Decode(
            new H264BitReader(bits.ToArray()), CreateInterHeader(0), 1,
            new H264MotionVectorPictureState(1, 1), 0, 0, 0));
    }

    [Fact]
    public void Decode_ShouldRejectIntraOrSkippedMacroblockHeaders()
    {
        BitWriter bits = new();
        H264MacroblockHeader intra = new(0, 5, false, 0,
            true, false, null, null, null, false);
        H264MacroblockHeader skipped = new(0, 0, true, 1,
            false, false, null, null, null, false);

        Assert.Throws<ArgumentException>(() => _decoder.Decode(
            new H264BitReader(bits.ToArray()), intra, 0,
            new H264MotionVectorPictureState(1, 1), 0, 0, 0));
        Assert.Throws<ArgumentException>(() => _decoder.Decode(
            new H264BitReader(bits.ToArray()), skipped, 0,
            new H264MotionVectorPictureState(1, 1), 0, 0, 0));
    }

    [Fact]
    public void DecodeSkippedRun_ShouldExpandRunAcrossRasterRows()
    {
        H264MacroblockHeader skippedRun = new(1, 0, true, 3,
            false, false, null, null, null, false);
        H264MotionVectorPictureState state = new(2, 2);

        IReadOnlyList<H264ReconstructedInterPartition> result =
            _decoder.DecodeSkippedRun(skippedRun, state, 11);

        Assert.Equal(3, result.Count);
        Assert.Equal(
            new[] { (4, 0), (0, 4), (4, 4) },
            result.Select(partition => (partition.X, partition.Y)));
        Assert.All(result, partition =>
            Assert.Equal(new H264MotionVector(0, 0), partition.MotionVector));
    }

    [Fact]
    public void DecodeSkippedRun_ShouldRejectRunPastPictureEnd()
    {
        H264MacroblockHeader skippedRun = new(3, 0, true, 2,
            false, false, null, null, null, false);

        Assert.Throws<FormatException>(() => _decoder.DecodeSkippedRun(
            skippedRun, new H264MotionVectorPictureState(2, 2), 0));
    }

    [Fact]
    public void DecodeSkipped_ShouldReconstructAndRecordPskipVector()
    {
        H264MacroblockHeader skipped = new(0, 0, true, 1,
            false, false, null, null, null, false);
        H264MotionVectorPictureState state = new(1, 1);

        H264ReconstructedInterPartition result =
            _decoder.DecodeSkipped(skipped, state, 0, 0, 9);

        Assert.Equal(new H264MotionVector(0, 0), result.MotionVector);
        Assert.Equal(0, result.ReferenceIndex);
    }

    private static H264MacroblockHeader CreateInterHeader(uint mbType) =>
        new(0, mbType, false, 0, false, false, null, null, null, false);

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteUe(uint value)
        {
            uint codeNum = checked(value + 1);
            int leadingZeroes = 0;
            for (uint current = codeNum; current > 1; current >>= 1)
                leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++)
                _bits.Add(false);
            WriteBits(codeNum, leadingZeroes + 1);
        }

        public void WriteSe(int value)
        {
            uint codeNum = value <= 0
                ? checked((uint)(-2L * value))
                : checked((uint)(2L * value - 1));
            WriteUe(codeNum);
        }

        private void WriteBits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                _bits.Add(((value >> i) & 1) != 0);
        }

        public byte[] ToArray()
        {
            byte[] bytes = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i])
                    bytes[i / 8] |= (byte)(1 << (7 - i % 8));
            return bytes;
        }
    }
}

using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BMacroblockSyntaxReaderTests
{
    [Fact]
    public void Read_BDirect16x16_ShouldNotConsumePredictionSyntax()
    {
        H264BitReader reader = new(new byte[] { 0x80 }, 1);
        H264BMacroblockSyntax result = new H264BMacroblockSyntaxReader().Read(
            reader, Header(0), 0, 0);
        Assert.Equal(H264BPredictionDirection.Direct, Assert.Single(result.Partitions).Direction);
        Assert.Null(result.Partitions[0].ReferenceIndexL0);
        Assert.Null(result.Partitions[0].ReferenceIndexL1);
        Assert.Equal(1, reader.RemainingBits);
    }

    [Fact]
    public void Read_BL0_16x16_ShouldReadOnlyList0ReferenceAndMvd()
    {
        BitWriter bits = new();
        bits.Se(2); bits.Se(-3);
        H264BMacroblockSyntax result = Read(bits, 1, 0, 0);
        H264BPredictionPartition partition = Assert.Single(result.Partitions);
        Assert.Equal(H264BPredictionDirection.List0, partition.Direction);
        Assert.Equal((uint)0, partition.ReferenceIndexL0);
        Assert.Null(partition.ReferenceIndexL1);
        Assert.Equal(new H264MotionVectorDifference(2, -3),
            partition.SubPartitions[0].MotionVectorDifferenceL0);
        Assert.Null(partition.SubPartitions[0].MotionVectorDifferenceL1);
    }

    [Fact]
    public void Read_BBi16x16_ShouldReadBothListMvdValuesInListOrder()
    {
        BitWriter bits = new();
        bits.Se(1); bits.Se(2); bits.Se(-3); bits.Se(-4);
        H264BPredictionPartition partition = Assert.Single(Read(bits, 3, 0, 0).Partitions);
        Assert.Equal(H264BPredictionDirection.Bi, partition.Direction);
        Assert.Equal(new H264MotionVectorDifference(1, 2), partition.SubPartitions[0].MotionVectorDifferenceL0);
        Assert.Equal(new H264MotionVectorDifference(-3, -4), partition.SubPartitions[0].MotionVectorDifferenceL1);
    }

    [Fact]
    public void Read_BL0L1_16x8_ShouldParseGroupedReferenceIndicesAndListMvd()
    {
        BitWriter bits = new();
        bits.Ue(1); // ref_idx_l0 partition 0
        bits.Ue(0); // ref_idx_l1 partition 1 (only non-default active list index is read below)
        bits.Se(1); bits.Se(2); // list0 vectors
        bits.Se(3); bits.Se(4); // list1 vectors
        H264BMacroblockSyntax result = Read(bits, 8, 1, 1);
        Assert.Equal(2, result.Partitions.Count);
        Assert.Equal((uint)1, result.Partitions[0].ReferenceIndexL0);
        Assert.Equal((uint)0, result.Partitions[1].ReferenceIndexL1);
        Assert.Equal(H264BPredictionDirection.List0, result.Partitions[0].Direction);
        Assert.Equal(H264BPredictionDirection.List1, result.Partitions[1].Direction);
    }

    [Fact]
    public void Read_B8x8_ShouldParseMixedDirectionSubMacroblocks()
    {
        BitWriter bits = new();
        bits.Ue(0); bits.Ue(1); bits.Ue(2); bits.Ue(3);
        // list0 ref index for sub-MB 1, then list1 ref index for sub-MB 2.
        bits.Ue(0); bits.Ue(0);
        // list0 MVDs for one 8x8 subpartition and four 4x4 subpartitions.
        for (int i = 0; i < 5; i++) { bits.Se(i); bits.Se(-i); }
        // list1 MVDs for one 8x8 subpartition and two 4x8 subpartitions.
        for (int i = 0; i < 3; i++) { bits.Se(-i); bits.Se(i); }
        H264BMacroblockSyntax result = Read(bits, 22, 0, 0);
        Assert.Equal(4, result.Partitions.Count);
        Assert.Equal(new[] { 1, 1, 1, 4 }, result.Partitions.Select(p => p.SubPartitions.Count));
        Assert.Equal(H264BPredictionDirection.Direct, result.Partitions[0].Direction);
        Assert.Equal(H264BPredictionDirection.List0, result.Partitions[1].Direction);
        Assert.Equal(H264BPredictionDirection.List1, result.Partitions[2].Direction);
        Assert.Equal(H264BPredictionDirection.Bi, result.Partitions[3].Direction);
    }

    [Fact]
    public void Read_ShouldRejectInvalidSubMacroblockTypeAndReferenceIndex()
    {
        BitWriter invalidType = new(); invalidType.Ue(13);
        Assert.Throws<FormatException>(() => Read(invalidType, 22, 0, 0));
        BitWriter invalidRef = new(); invalidRef.Ue(2);
        Assert.Throws<FormatException>(() => Read(invalidRef, 1, 1, 0));
    }

    [Fact]
    public void Read_ShouldRejectIntraSkippedAndUnsupportedTypes()
    {
        H264BMacroblockSyntaxReader reader = new();
        Assert.Throws<ArgumentException>(() => reader.Read(new H264BitReader(new byte[1]), Header(1, true), 0, 0));
        Assert.Throws<ArgumentException>(() => reader.Read(new H264BitReader(new byte[1]), Header(1, false, true), 0, 0));
        Assert.Throws<NotSupportedException>(() => reader.Read(new H264BitReader(new byte[1]), Header(23), 0, 0));
    }

    private static H264BMacroblockSyntax Read(BitWriter bits, uint type, uint l0, uint l1) =>
        new H264BMacroblockSyntaxReader().Read(bits.Reader(), Header(type), l0, l1);

    private static H264MacroblockHeader Header(uint type, bool intra = false, bool skip = false) =>
        new(type, type, skip, skip ? 1u : 0u, intra, false, null, null, null, false);

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
        public void Se(int value) => Ue(value <= 0
            ? checked((uint)(-2L * value))
            : checked((uint)(2L * value - 1)));
        public H264BitReader Reader()
        {
            byte[] data = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i]) data[i / 8] |= (byte)(1 << (7 - i % 8));
            return new H264BitReader(data, _bits.Count);
        }
    }
}

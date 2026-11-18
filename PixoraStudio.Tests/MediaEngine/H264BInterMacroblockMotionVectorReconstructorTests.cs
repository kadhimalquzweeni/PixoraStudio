using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BInterMacroblockMotionVectorReconstructorTests
{
    [Fact]
    public void Reconstruct_List0Partition_ShouldUseList0StateAndMvd()
    {
        H264BMacroblockSyntax syntax = Parse(1, 0, 0, (2, -3));
        var state0 = new H264MotionVectorPictureState(1, 1);
        var state1 = new H264MotionVectorPictureState(1, 1);

        H264ReconstructedBInterSubPartition result = Assert.Single(
            new H264BInterMacroblockMotionVectorReconstructor().Reconstruct(
                syntax, state0, state1, 0, 0, 0, 1, 1));

        Assert.Equal(new H264MotionVector(2, -3), result.MotionVectorL0);
        Assert.Null(result.MotionVectorL1);
        Assert.Equal(0, result.ReferenceIndexL0);
        Assert.Null(result.ReferenceIndexL1);
        Assert.NotNull(state0.GetCandidates(1, 0, 1, 1, 0).Left);
        Assert.Null(state1.GetCandidates(1, 0, 1, 1, 0).Left);
    }

    [Fact]
    public void Reconstruct_BiPartition_ShouldReconstructBothListsIndependently()
    {
        H264BMacroblockSyntax syntax = Parse(3, 0, 0, (1, 2), (-3, 4));
        var state0 = new H264MotionVectorPictureState(1, 1);
        var state1 = new H264MotionVectorPictureState(1, 1);

        H264ReconstructedBInterSubPartition result = Assert.Single(
            new H264BInterMacroblockMotionVectorReconstructor().Reconstruct(
                syntax, state0, state1, 0, 0, 0, 1, 1));

        Assert.Equal(new H264MotionVector(1, 2), result.MotionVectorL0);
        Assert.Equal(new H264MotionVector(-3, 4), result.MotionVectorL1);
        Assert.Equal(0, result.ReferenceIndexL0);
        Assert.Equal(0, result.ReferenceIndexL1);
    }

    [Fact]
    public void Reconstruct_B8x8_ShouldReconstructDifferentSubPartitionShapes()
    {
        H264BitReader reader = CreateReader(
            new uint[] { 1, 2, 3, 12 },
            Enumerable.Range(0, 6).SelectMany(i => new[] { i, -i })
                .Concat(Enumerable.Range(0, 6).SelectMany(i => new[] { -i, i })).ToArray());
        H264MacroblockHeader header = Header(22);
        H264BMacroblockSyntax syntax = new H264BMacroblockSyntaxReader().Read(reader, header, 0, 0);
        var state0 = new H264MotionVectorPictureState(1, 1);
        var state1 = new H264MotionVectorPictureState(1, 1);

        IReadOnlyList<H264ReconstructedBInterSubPartition> results =
            new H264BInterMacroblockMotionVectorReconstructor().Reconstruct(
                syntax, state0, state1, 0, 0, 0, 1, 1);

        Assert.Equal(9, results.Count); // 1 + 1 + 1 + 4 sub-partitions
        Assert.Equal(4, results.Count(item => item.Width == 1 && item.Height == 1));
        Assert.All(results.Where(item => item.ReferenceIndexL0 is not null),
            item => Assert.NotNull(item.MotionVectorL0));
        Assert.All(results.Where(item => item.ReferenceIndexL1 is not null),
            item => Assert.NotNull(item.MotionVectorL1));
    }

    [Fact]
    public void Reconstruct_ShouldRejectDirectPredictionUntilDirectStageExists()
    {
        H264BMacroblockSyntax syntax = new H264BMacroblockSyntaxReader().Read(
            new H264BitReader(new byte[] { 0x80 }, 1), Header(0), 0, 0);
        Assert.Throws<NotSupportedException>(() =>
            new H264BInterMacroblockMotionVectorReconstructor().Reconstruct(
                syntax, new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1), 0, 0, 0, 1, 1));
    }

    [Fact]
    public void Reconstruct_ShouldRejectInvalidListReferenceAndMismatchedPictureStates()
    {
        H264BMacroblockSyntax syntax = Parse(1, 1, 0, (0, 0));
        Assert.Throws<FormatException>(() =>
            new H264BInterMacroblockMotionVectorReconstructor().Reconstruct(
                syntax, new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(1, 1), 0, 0, 0, 1, 1));
        Assert.Throws<ArgumentException>(() =>
            new H264BInterMacroblockMotionVectorReconstructor().Reconstruct(
                syntax, new H264MotionVectorPictureState(1, 1),
                new H264MotionVectorPictureState(2, 1), 0, 0, 0, 2, 2));
    }

    private static H264BMacroblockSyntax Parse(uint type, uint refs0, uint refs1,
        params (int X, int Y)[] vectors)
    {
        List<int> values = [];
        foreach (var vector in vectors) { values.Add(vector.X); values.Add(vector.Y); }
        return new H264BMacroblockSyntaxReader().Read(
            CreateReader([], values.ToArray()), Header(type), refs0, refs1);
    }

    private static H264MacroblockHeader Header(uint type) =>
        new(type, type, false, 0, false, false, null, null, null, false);

    private static H264BitReader CreateReader(IEnumerable<uint> unsignedValues, params int[] signedValues)
    {
        BitWriter writer = new();
        foreach (uint value in unsignedValues) writer.Ue(value);
        foreach (int value in signedValues) writer.Se(value);
        return writer.Reader();
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

using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacInterMacroblockResidualSyntaxReaderTests
{
    [Fact]
    public void Read_WithZeroCodedBlockPattern_ShouldNotReadTransformOrQpDelta()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0, 0, 0, 0, 0 });
        H264CabacIntraMacroblockNeighbourContext neighbours = new();
        H264CabacInterMacroblockResidualSyntaxReader reader =
            new(new H264CabacSyntaxElementReader(bins), neighbours);

        H264InterMacroblockResidualSyntax result = reader.Read(
            Sps(), Pps() with { EntropyCodingModeFlag = true }, 0, 0, 1, 0, noSubMacroblockPartitionSmallerThan8x8: true);

        Assert.Equal(0u, result.CodedBlockPattern.Luma);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.False(result.TransformSize8X8Flag);
        Assert.Null(result.MacroblockQpDelta);
        Assert.Equal(5, bins.ContextIndices.Count);
    }

    [Fact]
    public void Read_WithCodedLumaAndTransformEnabled_ShouldReadTransformAndQpDelta()
    {
        ScriptedCabacReader bins = new(new[] { 1, 0, 0, 0, 0, 0, 1, 0 });
        H264CabacIntraMacroblockNeighbourContext neighbours = new();
        H264CabacInterMacroblockResidualSyntaxReader reader =
            new(new H264CabacSyntaxElementReader(bins), neighbours);

        H264InterMacroblockResidualSyntax result = reader.Read(
            Sps(), Pps() with { EntropyCodingModeFlag = true, Transform8X8ModeFlag = true },
            0, 0, 1, 0, noSubMacroblockPartitionSmallerThan8x8: true);

        Assert.Equal(1u, result.CodedBlockPattern.Luma);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.False(result.TransformSize8X8Flag);
        Assert.Equal(1, result.MacroblockQpDelta);
        Assert.Equal(new[] { 73, 74, 75, 76, 77, 399, 60, 62 },
            bins.ContextIndices);
    }

    [Fact]
    public void Read_WithSmallSubPartitions_ShouldNotConsumeTransformFlag()
    {
        ScriptedCabacReader bins = new(new[] { 1, 0, 0, 0, 0, 0 });
        H264CabacIntraMacroblockNeighbourContext neighbours = new();
        H264CabacInterMacroblockResidualSyntaxReader reader =
            new(new H264CabacSyntaxElementReader(bins), neighbours);

        H264InterMacroblockResidualSyntax result = reader.Read(
            Sps(), Pps() with { EntropyCodingModeFlag = true, Transform8X8ModeFlag = true },
            0, 0, 1, 0, noSubMacroblockPartitionSmallerThan8x8: false);

        Assert.False(result.TransformSize8X8Flag);
        Assert.Equal(0, result.MacroblockQpDelta);
        Assert.Equal(6, bins.ContextIndices.Count);
    }


    [Fact]
    public void Read_ShouldDeriveChromaArrayTypeFromSpsFor422()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0, 0, 0, 0, 0 });
        H264CabacInterMacroblockResidualSyntaxReader reader =
            new(new H264CabacSyntaxElementReader(bins),
                new H264CabacIntraMacroblockNeighbourContext());

        H264SequenceParameterSet sps = Sps() with { ChromaFormatIdc = 2 };
        H264InterMacroblockResidualSyntax result = reader.Read(
            sps, Pps() with { EntropyCodingModeFlag = true },
            0, 0, 0, 0, noSubMacroblockPartitionSmallerThan8x8: true);

        Assert.Equal(2u, result.CodedBlockPattern.ChromaArrayType);
        Assert.Equal(6, bins.ContextIndices.Count);
    }

    [Fact]
    public void Read_ShouldUseNoPreviousMacroblockForFirstAddress()
    {
        ScriptedCabacReader bins = new(new[] { 1, 0, 0, 0, 0, 0, 0, 1, 0 });
        H264CabacIntraMacroblockNeighbourContext neighbours = new();
        H264CabacInterMacroblockResidualSyntaxReader reader =
            new(new H264CabacSyntaxElementReader(bins), neighbours);

        H264InterMacroblockResidualSyntax result = reader.Read(
            Sps(), Pps() with { EntropyCodingModeFlag = true, Transform8X8ModeFlag = true },
            0, 0, 0, 0, noSubMacroblockPartitionSmallerThan8x8: true);

        Assert.Equal(1, result.MacroblockQpDelta);
    }

    private static H264SequenceParameterSet Sps() =>
        new(0, 100, 40, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private static H264PictureParameterSetHeader Pps() =>
        new(0, 0, false, false, 0);

    private sealed class ScriptedCabacReader : IH264CabacBinReader
    {
        private readonly Queue<int> _bins;
        public ScriptedCabacReader(IEnumerable<int> bins) => _bins = new Queue<int>(bins);
        public List<int> ContextIndices { get; } = [];
        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _bins.Dequeue();
        }
        public int DecodeBypass() => _bins.Dequeue();
        public int DecodeTerminate() => _bins.Dequeue();
        public void MarkTerminated() { }
    }
}

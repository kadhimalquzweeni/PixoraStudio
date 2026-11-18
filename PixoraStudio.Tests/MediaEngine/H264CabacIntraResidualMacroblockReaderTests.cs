using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacIntraResidualMacroblockReaderTests
{
    [Fact]
    public void Read_ShouldDecodeIntra16x16DcAndReturnSharedResidualModel()
    {
        ScriptedCabacReader bins = new(0);
        H264CabacResidualNeighbourContext neighbours = new();
        H264MacroblockHeader header = new(
            0, 1, false, 0, true, true, 0, 0, 0, false);
        H264IntraMacroblockSyntax syntax = new(
            null, null, 0,
            new H264CodedBlockPattern(0, 0, 0, 1),
            0);

        H264IntraLumaResidualMacroblock result =
            new H264CabacIntraResidualMacroblockReader().Read(
                bins, header, syntax, CreateSps(1), 0, 0, 1, neighbours);

        Assert.NotNull(result.Intra16x16Dc);
        Assert.Equal(16, result.Intra16x16Dc!.MaximumCoefficients);
        Assert.Equal(0, result.Intra16x16Dc.TotalCoefficients);
        Assert.Equal(16, result.Blocks.Count);
        Assert.All(result.Blocks, block => Assert.False(block.IsCoded));
        Assert.Equal(2, result.ChromaComponents.Count);
        Assert.All(result.ChromaComponents, component => Assert.Null(component.Dc));
        Assert.Equal(new[] { 88 }, bins.ContextIndices);
    }

    [Fact]
    public void Read_ShouldDecodeCodedLuma4x4GroupUsingNeighbourCbfContexts()
    {
        ScriptedCabacReader bins = new(0, 0, 0, 0);
        H264CabacResidualNeighbourContext neighbours = new();
        H264MacroblockHeader header = CreateIntraNxNHeader();
        H264IntraMacroblockSyntax syntax = new(
            false, null, 0,
            new H264CodedBlockPattern(1, 1, 0, 1),
            null);

        H264IntraLumaResidualMacroblock result =
            new H264CabacIntraResidualMacroblockReader().Read(
                bins, header, syntax, CreateSps(1), 0, 0, 1, neighbours);

        Assert.Equal(16, result.Blocks.Count);
        Assert.Equal(4, result.Blocks.Count(block => block.IsCoded));
        Assert.Equal(new[] { 96, 95, 94, 93 }, bins.ContextIndices);
        Assert.All(result.Blocks.Where(block => block.IsCoded),
            block => Assert.Equal(0, block.Residual.TotalCoefficients));
    }

    [Fact]
    public void Read_ShouldDecodeChromaDcFlagsForBothComponents()
    {
        ScriptedCabacReader bins = new(0, 0);
        H264CabacResidualNeighbourContext neighbours = new();
        H264IntraMacroblockSyntax syntax = new(
            false, null, 0,
            new H264CodedBlockPattern(16, 0, 1, 1),
            null);

        H264IntraLumaResidualMacroblock result =
            new H264CabacIntraResidualMacroblockReader().Read(
                bins, CreateIntraNxNHeader(), syntax,
                CreateSps(1), 0, 0, 1, neighbours);

        Assert.All(result.ChromaComponents,
            component => Assert.Equal(4, component.Dc!.MaximumCoefficients));
        Assert.All(result.ChromaComponents,
            component => Assert.Equal(0, component.Dc!.TotalCoefficients));
        Assert.Equal(new[] { 100, 100 }, bins.ContextIndices);
    }

    [Fact]
    public void Read_ShouldUseEightCoefficientChromaDcMapFor422()
    {
        ScriptedCabacReader bins = new(0, 0);
        H264CabacResidualNeighbourContext neighbours = new();
        H264IntraMacroblockSyntax syntax = new(
            false, null, 0,
            new H264CodedBlockPattern(16, 0, 1, 2),
            null);

        H264IntraLumaResidualMacroblock result =
            new H264CabacIntraResidualMacroblockReader().Read(
                bins, CreateIntraNxNHeader(), syntax,
                CreateSps(2), 0, 0, 1, neighbours);

        Assert.All(result.ChromaComponents,
            component => Assert.Equal(8, component.Dc!.MaximumCoefficients));
        Assert.Equal(new[] { 100, 100 }, bins.ContextIndices);
    }

    [Fact]
    public void Read_ShouldCreateFourEmptyEightByEightBlocksWithoutCbfBins()
    {
        ScriptedCabacReader bins = new();
        H264CabacResidualNeighbourContext neighbours = new();
        H264IntraMacroblockSyntax syntax = new(
            true, new H264IntraPredictionModeSyntax(true,
                Enumerable.Repeat(new H264IntraPredictionModeEntry(true, null), 4).ToArray()),
            0,
            new H264CodedBlockPattern(0, 0, 0, 1),
            null);

        H264IntraLumaResidualMacroblock result =
            new H264CabacIntraResidualMacroblockReader().Read(
                bins, CreateIntraNxNHeader(), syntax,
                CreateSps(1), 0, 0, 1, neighbours);

        Assert.Empty(result.Blocks);
        Assert.Equal(4, result.Intra8x8Blocks.Count);
        Assert.All(result.Intra8x8Blocks, block =>
        {
            Assert.False(block.IsCoded);
            Assert.Equal(64, block.CoefficientsInScanOrder.Count);
            Assert.All(block.CoefficientsInScanOrder, value => Assert.Equal(0, value));
        });
        Assert.Empty(bins.ContextIndices);
    }


    [Fact]
    public void Read_ShouldDecodeCodedEightByEightBlockWithoutCodedBlockFlag()
    {
        int[] decisions = Enumerable.Repeat(0, 63).Append(0).ToArray();
        ScriptedCabacReader bins = new(decisions);
        H264IntraMacroblockSyntax syntax = new(
            true,
            new H264IntraPredictionModeSyntax(true,
                Enumerable.Repeat(new H264IntraPredictionModeEntry(true, null), 4).ToArray()),
            0,
            new H264CodedBlockPattern(1, 1, 0, 1),
            null);

        H264IntraLumaResidualMacroblock result =
            new H264CabacIntraResidualMacroblockReader().Read(
                bins, CreateIntraNxNHeader(), syntax,
                CreateSps(1), 0, 0, 1,
                new H264CabacResidualNeighbourContext());

        Assert.True(result.Intra8x8Blocks[0].IsCoded);
        Assert.Equal(1, result.Intra8x8Blocks[0].CoefficientsInScanOrder[63]);
        Assert.Equal(64, bins.ContextIndices.Count);
        Assert.Equal(402, bins.ContextIndices[0]);
        Assert.Equal(427, bins.ContextIndices[^1]);
    }

    [Fact]
    public void RecordPcmMacroblock_ShouldMarkNeighbourBlocksAsCoded()
    {
        H264CabacResidualNeighbourContext neighbours = new();

        new H264CabacIntraResidualMacroblockReader().RecordPcmMacroblock(
            0, 0, 1, CreateSps(1), neighbours);

        Assert.Equal(3, neighbours.GetContextIndexIncrement(
            0, 1, 0, 4, 4, 1, currentMacroblockIsIntra: true));
    }

    private static H264MacroblockHeader CreateIntraNxNHeader() =>
        new(0, 0, false, 0, true, false, null, null, null, false);

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) => new(
        0, 100, 42, chromaFormat, false, 0, 0, 0, 2, 0, 1, 1, 1,
        true, true, 32, 32, false);

    private sealed class ScriptedCabacReader(params int[] bins) : IH264CabacBinReader
    {
        private readonly Queue<int> _bins = new(bins);
        public List<int> ContextIndices { get; } = [];

        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _bins.Dequeue();
        }

        public int DecodeBypass() => _bins.Dequeue();
        public int DecodeTerminate() => throw new InvalidOperationException();
        public void MarkTerminated() => throw new InvalidOperationException();
    }
}

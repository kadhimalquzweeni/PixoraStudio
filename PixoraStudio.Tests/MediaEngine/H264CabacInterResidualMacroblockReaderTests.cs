using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacInterResidualMacroblockReaderTests
{
    [Fact]
    public void Read_WithZeroCodedBlockPattern_ShouldReturnEmptyInterResidual()
    {
        ScriptedCabacReader bins = new();
        H264InterResidualMacroblock result = CreateReader().Read(
            bins, Header(), Syntax(0, 0, false), Sps(), 0, 0, 1,
            new H264CabacResidualNeighbourContext());

        Assert.Equal(16, result.LumaBlocks.Count);
        Assert.All(result.LumaBlocks, block => Assert.False(block.IsCoded));
        Assert.Equal(2, result.ChromaComponents.Count);
        Assert.All(result.ChromaComponents, component =>
        {
            Assert.Null(component.Dc);
            Assert.Equal(4, component.Blocks.Count);
            Assert.All(component.Blocks, block => Assert.False(block.IsCoded));
        });
        Assert.Empty(bins.ContextIndices);
    }

    [Fact]
    public void Read_WithCodedLumaGroup_ShouldReadFourCodedBlockFlags()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0, 0, 0 });
        H264InterResidualMacroblock result = CreateReader().Read(
            bins, Header(), Syntax(1, 0, false), Sps(), 0, 0, 1,
            new H264CabacResidualNeighbourContext());

        Assert.Equal(16, result.LumaBlocks.Count);
        Assert.All(result.LumaBlocks.Take(4), block => Assert.True(block.IsCoded));
        Assert.All(result.LumaBlocks.Skip(4), block => Assert.False(block.IsCoded));
        Assert.All(result.LumaBlocks.Take(4), block => Assert.Equal(0, block.Residual.TotalCoefficients));
        Assert.Equal(new[] { 93, 93, 93, 93 }, bins.ContextIndices);
    }

    [Fact]
    public void Read_WithNonZeroLumaDcCoefficient_ShouldDecodeSignificanceLevelAndSign()
    {
        ScriptedCabacReader bins = new(
            new[] { 1, 1, 1, 0, 0, 0, 0 },
            new[] { 0 });
        H264InterResidualMacroblock result = CreateReader().Read(
            bins, Header(), Syntax(1, 0, false), Sps(), 0, 0, 1,
            new H264CabacResidualNeighbourContext());

        H264CavlcResidualBlock firstBlock = result.LumaBlocks[0].Residual;
        Assert.Equal(1, firstBlock.TotalCoefficients);
        Assert.Equal(1, firstBlock.Coefficients[0]);
        Assert.All(result.LumaBlocks.Skip(1), block =>
            Assert.Equal(0, block.Residual.TotalCoefficients));
        Assert.Equal(new[] { 93, 134, 195, 248, 94, 95, 93 }, bins.ContextIndices);
    }

    [Fact]
    public void Read_WithEightByEightTransform_ShouldUseSignificanceMapWithoutCbf()
    {
        ScriptedCabacReader bins = new(
            decisionBins: Enumerable.Repeat(0, 64),
            bypassBins: new[] { 0 });
        H264InterResidualMacroblock result = CreateReader().Read(
            bins, Header(), Syntax(1, 0, true), Sps(), 0, 0, 1,
            new H264CabacResidualNeighbourContext());

        Assert.Empty(result.LumaBlocks);
        Assert.Equal(4, result.Luma8x8Blocks.Count);
        Assert.True(result.Luma8x8Blocks[0].IsCoded);
        Assert.All(result.Luma8x8Blocks.Skip(1), block => Assert.False(block.IsCoded));
        Assert.Equal(1, result.Luma8x8Blocks[0].CoefficientsInScanOrder[63]);
        Assert.Equal(64, bins.ContextIndices.Count);
        Assert.Equal(402, bins.ContextIndices[0]);
    }

    [Fact]
    public void Read_WithChromaDcOnly_ShouldReadCbAndCrCodedBlockFlags()
    {
        ScriptedCabacReader bins = new(new[] { 0, 0 });
        H264InterResidualMacroblock result = CreateReader().Read(
            bins, Header(), Syntax(0, 1, false), Sps(), 0, 0, 1,
            new H264CabacResidualNeighbourContext());

        Assert.All(result.ChromaComponents, component =>
        {
            Assert.NotNull(component.Dc);
            Assert.Equal(0, component.Dc!.TotalCoefficients);
            Assert.All(component.Blocks, block => Assert.False(block.IsCoded));
        });
        Assert.Equal(new[] { 97, 97 }, bins.ContextIndices);
    }

    [Fact]
    public void Read_WithChromaDcAndAc_ShouldReadAllChromaCodedBlockFlags()
    {
        ScriptedCabacReader bins = new(Enumerable.Repeat(0, 10).ToArray());
        H264InterResidualMacroblock result = CreateReader().Read(
            bins, Header(), Syntax(0, 2, false), Sps(), 0, 0, 1,
            new H264CabacResidualNeighbourContext());

        Assert.All(result.ChromaComponents, component =>
        {
            Assert.NotNull(component.Dc);
            Assert.Equal(4, component.Blocks.Count);
            Assert.All(component.Blocks, block => Assert.True(block.IsCoded));
        });
        Assert.Equal(new[] { 97, 97, 101, 101, 101, 101, 101, 101, 101, 101 },
            bins.ContextIndices);
    }

    private static H264CabacInterResidualMacroblockReader CreateReader() => new();

    private static H264MacroblockHeader Header() =>
        new(0, 0, false, 0, false, false, null, null, null, false);

    private static H264InterMacroblockResidualSyntax Syntax(
        uint luma, uint chroma, bool transform) =>
        new(new H264CodedBlockPattern(luma + (16 * chroma), luma, chroma, 1), null)
        {
            TransformSize8X8Flag = transform
        };

    private static H264SequenceParameterSet Sps() =>
        new(0, 100, 40, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

    private sealed class ScriptedCabacReader : IH264CabacBinReader
    {
        private readonly Queue<int> _decisionBins;
        private readonly Queue<int> _bypassBins;

        public ScriptedCabacReader()
            : this(Array.Empty<int>(), Array.Empty<int>())
        {
        }

        public ScriptedCabacReader(params int[] bins)
            : this(bins, Array.Empty<int>())
        {
        }

        public ScriptedCabacReader(
            IEnumerable<int> decisionBins,
            IEnumerable<int> bypassBins)
        {
            _decisionBins = new Queue<int>(decisionBins);
            _bypassBins = new Queue<int>(bypassBins);
        }

        public List<int> ContextIndices { get; } = [];
        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _decisionBins.Dequeue();
        }
        public int DecodeBypass() => _bypassBins.Dequeue();
        public int DecodeTerminate() => throw new InvalidOperationException();
        public void MarkTerminated() => throw new InvalidOperationException();
    }
}

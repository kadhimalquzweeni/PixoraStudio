using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacResidualBlockReaderTests
{
    [Fact]
    public void ReadBlock_ShouldInferFinalCoefficientWhenNoLastFlagIsSet()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0, 0, 0 },
            bypassBins: new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, 4, 0, 3, 105, 166, 227,
            new[] { 0, 1, 2 }, new[] { 0, 1, 2 });

        Assert.Equal(new[] { 0, 0, 0, 1 }, coefficients);
        Assert.Equal(new[] { 105, 106, 107, 228 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBlock_ShouldStopAtLastSignificantCoefficientAndDecodeReverseLevels()
    {
        // Position 0 is significant and last; its abs_level_minus1 is zero,
        // followed by a positive sign bypass bin.
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 1, 1, 0 },
            bypassBins: new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, 4, 0, 3, 105, 166, 227,
            new[] { 0, 0, 0 }, new[] { 0, 0, 0 });

        Assert.Equal(new[] { 1, 0, 0, 0 }, coefficients);
        Assert.Equal(new[] { 105, 166, 228 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBlock_ShouldDecodeNegativeCoefficient()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0 },
            bypassBins: new[] { 1 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, 1, 0, 0, 105, 166, 227,
            Array.Empty<int>(), Array.Empty<int>());

        Assert.Equal(new[] { -1 }, coefficients);
    }

    [Fact]
    public void ReadBlock_ShouldDecodeMultipleSignificantCoefficientsInReverseOrder()
    {
        // Both positions are significant. The higher position is +1;
        // the lower position is -2.
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 1, 0, 1, 1, 0 },
            bypassBins: new[] { 0, 1 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, 3, 0, 2, 105, 166, 227,
            new[] { 0, 1 }, new[] { 0, 1 });

        Assert.Equal(new[] { -2, 1, 0 }, coefficients);
        Assert.Equal(new[] { 105, 166, 106, 167, 228, 229, 232 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBlock_ShouldRejectInvalidContextMapLengthBeforeConsumingBins()
    {
        ScriptedCabacReader bins = new();
        H264CabacResidualBlockReader reader = new();

        Assert.Throws<ArgumentException>(() => reader.ReadBlock(
            bins, 4, 0, 3, 105, 166, 227,
            new[] { 0 }, new[] { 0, 0, 0 }));
        Assert.Empty(bins.ContextIndices);
    }

    [Fact]
    public void ReadBlock_ShouldRejectNegativeContextIncrementBeforeConsumingBins()
    {
        ScriptedCabacReader bins = new();
        H264CabacResidualBlockReader reader = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadBlock(
            bins, 4, 0, 3, 105, 166, 227,
            new[] { 0, -1, 0 }, new[] { 0, 0, 0 }));
        Assert.Empty(bins.ContextIndices);
    }

    [Fact]
    public void ReadBlock_ShouldRejectInvalidBypassSignBin()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0 },
            bypassBins: new[] { 2 });
        H264CabacResidualBlockReader reader = new();

        Assert.Throws<FormatException>(() => reader.ReadBlock(
            bins, 1, 0, 0, 105, 166, 227,
            Array.Empty<int>(), Array.Empty<int>()));
    }


    [Fact]
    public void ReadBlock_ShouldDecodeEscapeLevelWithZeroOrderSuffix()
    {
        // coeff_abs_level_minus1 escape value 14: fourteen context-coded
        // prefix bins, then a zero bypass escape-order terminator.
        int[] decisions = new[] { 1 }.Concat(Enumerable.Repeat(1, 13)).ToArray();
        ScriptedCabacReader bins = new(
            decisionBins: decisions,
            bypassBins: new[] { 0, 0 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, 1, 0, 0, 105, 166, 227,
            Array.Empty<int>(), Array.Empty<int>());

        Assert.Equal(new[] { 15 }, coefficients);
    }


    [Fact]
    public void ReadBlock_WithCategoryMap_ShouldReturnSharedResidualModel()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0 },
            bypassBins: new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        var block = reader.ReadBlock(
            bins, H264CabacResidualBlockContext.Create(2));

        Assert.Equal(16, block.MaximumCoefficients);
        Assert.Equal(1, block.TotalCoefficients);
        Assert.Equal(1, block.Coefficients[15]);
    }

    [Fact]
    public void ReadBlock_WithUncodedCategory_ShouldNotConsumeBins()
    {
        ScriptedCabacReader bins = new();
        H264CabacResidualBlockReader reader = new();

        var block = reader.ReadBlock(
            bins, H264CabacResidualBlockContext.Create(2), codedBlockFlag: false);

        Assert.Equal(0, block.TotalCoefficients);
        Assert.Equal(16, block.Coefficients.Count);
        Assert.Empty(bins.ContextIndices);
    }

    [Fact]
    public void ReadBlock_WithEightByEightCategory_ShouldRejectExplicitUncodedFlag()
    {
        ScriptedCabacReader bins = new();
        H264CabacResidualBlockReader reader = new();

        Assert.Throws<ArgumentException>(() => reader.ReadBlock(
            bins, H264CabacResidualBlockContext.Create(5), codedBlockFlag: false));
        Assert.Empty(bins.ContextIndices);
    }


    [Fact]
    public void ReadBlockWithCodedBlockFlag_ShouldUseCategorySpecificContext()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        var block = reader.ReadBlockWithCodedBlockFlag(
            bins, H264CabacResidualBlockContext.Create(2), contextIndexIncrement: 3);

        Assert.Equal(new[] { 96 }, bins.ContextIndices);
        Assert.Equal(0, block.TotalCoefficients);
    }

    [Fact]
    public void ReadBlockWithCodedBlockFlag_ShouldNotReadResidualWhenFlagIsZero()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        var block = reader.ReadBlockWithCodedBlockFlag(
            bins, H264CabacResidualBlockContext.Create(1), contextIndexIncrement: 2);

        Assert.Equal(0, block.TotalCoefficients);
        Assert.Equal(15, block.Coefficients.Count);
        Assert.Equal(new[] { 91 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBlockWithCodedBlockFlag_ShouldUseFixedContextForLumaDc()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        _ = reader.ReadBlockWithCodedBlockFlag(
            bins, H264CabacResidualBlockContext.Create(0), contextIndexIncrement: 0);

        Assert.Equal(new[] { 85 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBlockWithCodedBlockFlag_ShouldRejectInvalidIncrementBeforeConsumingBins()
    {
        ScriptedCabacReader bins = new();
        H264CabacResidualBlockReader reader = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reader.ReadBlockWithCodedBlockFlag(
                bins, H264CabacResidualBlockContext.Create(2), contextIndexIncrement: 4));
        Assert.Empty(bins.ContextIndices);
    }

    [Fact]
    public void ReadBlockWithCodedBlockFlag_ShouldOmitFlagForLumaEightByEight()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0 },
            bypassBins: new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        var block = reader.ReadBlockWithCodedBlockFlag(
            bins, H264CabacResidualBlockContext.Create(5), contextIndexIncrement: 0);

        Assert.Equal(1, block.TotalCoefficients);
        Assert.Empty(bins.ContextIndices);
    }


    [Fact]
    public void ReadBlock_ShouldTransitionLevelContextAfterMagnitudeGreaterThanOne()
    {
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 1, 0, 1, 0, 0 },
            bypassBins: new[] { 0, 0 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, 2, 0, 1, 105, 166, 227,
            new[] { 0 }, new[] { 0 });

        Assert.Equal(new[] { 1, 2 }, coefficients);
        Assert.Equal(new[] { 105, 166, 228, 232, 231 }, bins.ContextIndices);
    }


    [Fact]
    public void ReadBlock_ShouldUseSpecial422ChromaDcGreaterThanOneContextAtHighNodeState()
    {
        int[] decisions =
        [
            1, 0, 1, 0, 1, 0, 1, 0, 0, 0, // significance and last flags
            1, 0, 1, 0, 1, 0, 1, 0, 1, 0 // five coefficient levels
        ];
        ScriptedCabacReader bins = new(
            decisionBins: decisions,
            bypassBins: new[] { 0, 0, 0, 0, 0 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, H264CabacResidualBlockContext.Create(3, chroma422: true),
            codedBlockFlag: true).Coefficients.ToArray();

        Assert.Equal(new[] { 2, 2, 2, 2, 0, 0, 0, 2 }, coefficients);
        Assert.Equal(265, bins.ContextIndices[^1]);
    }


    [Fact]
    public void ReadBlockWithCodedBlockFlag_ShouldApplyNeighbourContextToLumaDc()
    {
        ScriptedCabacReader bins = new(new[] { 0 });
        H264CabacResidualBlockReader reader = new();

        _ = reader.ReadBlockWithCodedBlockFlag(
            bins, H264CabacResidualBlockContext.Create(0), contextIndexIncrement: 3);

        Assert.Equal(new[] { 88 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadBlockWithCodedBlockFlag_ShouldRejectContextForImplicitEightByEightFlag()
    {
        ScriptedCabacReader bins = new();
        H264CabacResidualBlockReader reader = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reader.ReadBlockWithCodedBlockFlag(
                bins, H264CabacResidualBlockContext.Create(5), contextIndexIncrement: 1));
        Assert.Empty(bins.ContextIndices);
    }

    private sealed class ScriptedCabacReader : IH264CabacBinReader
    {
        private readonly Queue<int> _decisionBins;
        private readonly Queue<int> _bypassBins;

        public ScriptedCabacReader(
            IEnumerable<int>? decisionBins = null,
            IEnumerable<int>? bypassBins = null)
        {
            _decisionBins = new Queue<int>(decisionBins ?? []);
            _bypassBins = new Queue<int>(bypassBins ?? []);
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

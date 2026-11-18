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
        Assert.Equal(new[] { 105, 120, 136 }, bins.ContextIndices);
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
    public void ReadBlock_ShouldUseUpdatedLevelContextsForSubsequentCoefficients()
    {
        // Two inferred significant coefficients, decoded from high to low.
        // First is +1; second is -2 (first level bin 1, then terminating 0).
        ScriptedCabacReader bins = new(
            decisionBins: new[] { 0, 1, 0 },
            bypassBins: new[] { 0, 1, 0 });
        H264CabacResidualBlockReader reader = new();

        int[] coefficients = reader.ReadBlock(
            bins, 2, 0, 1, 105, 166, 227,
            new[] { 0 }, new[] { 0 });

        Assert.Equal(new[] { -2, 1 }, coefficients);
        Assert.Equal(new[] { 105, 228, 233 }, bins.ContextIndices);
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

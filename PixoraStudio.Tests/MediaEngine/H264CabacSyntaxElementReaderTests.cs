using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacSyntaxElementReaderTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, -1)]
    [InlineData(3, 2)]
    [InlineData(4, -2)]
    [InlineData(49, 25)]
    [InlineData(50, -25)]
    [InlineData(52, -26)]
    public void ReadMacroblockQpDelta_ShouldMapUnaryCodeNumberToSignedDelta(
        int codeNum, int expectedDelta)
    {
        ScriptedCabacReader bins = new(Enumerable.Repeat(1, codeNum).Append(0));
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Equal(expectedDelta, reader.ReadMacroblockQpDelta(0));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 61)]
    [InlineData(2, 62)]
    public void ReadMacroblockQpDelta_ShouldApplyNeighbourContextIncrement(
        int increment, int expectedFirstContext)
    {
        ScriptedCabacReader bins = new(0);
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Equal(0, reader.ReadMacroblockQpDelta(increment));
        Assert.Equal(new[] { expectedFirstContext }, bins.ContextIndices);
    }

    [Fact]
    public void ReadMacroblockQpDelta_ShouldReuseThirdContextForRemainingPrefixBins()
    {
        ScriptedCabacReader bins = new(new[] { 1, 1, 1, 0 });
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Equal(2, reader.ReadMacroblockQpDelta(1));
        Assert.Equal(new[] { 61, 62, 63, 63 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadMacroblockQpDelta_ShouldRejectPositiveDeltaAbove25()
    {
        ScriptedCabacReader bins = new(Enumerable.Repeat(1, 51).Append(0));
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Throws<FormatException>(() => reader.ReadMacroblockQpDelta(0));
    }

    [Fact]
    public void ReadMacroblockQpDelta_ShouldRejectCodeOutsideSupportedQpRange()
    {
        ScriptedCabacReader bins = new(Enumerable.Repeat(1, 53));
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Throws<FormatException>(() => reader.ReadMacroblockQpDelta(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ReadMacroblockQpDelta_ShouldRejectInvalidNeighbourContextIncrement(int increment)
    {
        H264CabacSyntaxElementReader reader = new(new ScriptedCabacReader(0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reader.ReadMacroblockQpDelta(increment));
    }

    private sealed class ScriptedCabacReader : IH264CabacBinReader
    {
        private readonly Queue<int> _bins;

        public ScriptedCabacReader(params int[] bins) : this((IEnumerable<int>)bins) { }

        public ScriptedCabacReader(IEnumerable<int> bins)
        {
            _bins = new Queue<int>(bins);
        }

        public List<int> ContextIndices { get; } = [];

        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _bins.Dequeue();
        }

        public int DecodeBypass() => throw new NotSupportedException();
        public int DecodeTerminate() => throw new NotSupportedException();
    }
}

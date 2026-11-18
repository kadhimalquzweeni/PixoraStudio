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


    [Fact]
    public void ReadPreviousIntraPredictionModeFlag_ShouldUseContext68()
    {
        ScriptedCabacReader bins = new(1);
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.True(reader.ReadPreviousIntraPredictionModeFlag());
        Assert.Equal(new[] { 68 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadRemainingIntraPredictionMode_ShouldReadThreeBinsFromContext69()
    {
        ScriptedCabacReader bins = new(new[] { 1, 0, 1 });
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Equal(5, reader.ReadRemainingIntraPredictionMode());
        Assert.Equal(new[] { 69, 69, 69 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadTransformSize8x8Flag_ShouldUseContext399()
    {
        ScriptedCabacReader bins = new(1);
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.True(reader.ReadTransformSize8x8Flag());
        Assert.Equal(new[] { 399 }, bins.ContextIndices);
    }

    [Fact]
    public void ReadIntraChromaPredictionMode_ShouldDecodeTruncatedUnary()
    {
        (int[] Bins, int Increment, int Expected)[] cases =
        [
            (new[] { 0 }, 0, 0),
            (new[] { 1, 0 }, 1, 1),
            (new[] { 1, 1, 0 }, 2, 2),
            (new[] { 1, 1, 1 }, 2, 3)
        ];

        foreach ((int[] encodedBins, int contextIncrement, int expectedMode) in cases)
        {
            ScriptedCabacReader bins = new(encodedBins);
            H264CabacSyntaxElementReader reader = new(bins);

            Assert.Equal(expectedMode, reader.ReadIntraChromaPredictionMode(contextIncrement));
            Assert.Equal(encodedBins.Length, bins.ContextIndices.Count);
        }
    }

    [Fact]
    public void ReadIntraChromaPredictionMode_ShouldUseLastContextForRemainingBins()
    {
        ScriptedCabacReader bins = new(new[] { 1, 1, 0 });
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Equal(2, reader.ReadIntraChromaPredictionMode(1));
        Assert.Equal(new[] { 65, 66, 66 }, bins.ContextIndices);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ReadIntraChromaPredictionMode_ShouldRejectInvalidContextIncrement(int increment)
    {
        H264CabacSyntaxElementReader reader = new(new ScriptedCabacReader(0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reader.ReadIntraChromaPredictionMode(increment));
    }


    [Theory]
    [InlineData(false, 0, 11)]
    [InlineData(false, 1, 12)]
    [InlineData(false, 2, 13)]
    [InlineData(true, 0, 24)]
    [InlineData(true, 1, 25)]
    [InlineData(true, 2, 26)]
    public void ReadMacroblockSkipFlag_ShouldSelectSliceAndNeighbourContext(
        bool isBSlice, int increment, int expectedContext)
    {
        ScriptedCabacReader bins = new(1);
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.True(reader.ReadMacroblockSkipFlag(isBSlice, increment));
        Assert.Equal(new[] { expectedContext }, bins.ContextIndices);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ReadMacroblockSkipFlag_ShouldRejectInvalidContextIncrement(int increment)
    {
        H264CabacSyntaxElementReader reader = new(new ScriptedCabacReader(0));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            reader.ReadMacroblockSkipFlag(false, increment));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ReadEndOfSliceFlag_ShouldDecodeTerminatingBin(int bin, bool expected)
    {
        ScriptedCabacReader bins = new(bin);
        H264CabacSyntaxElementReader reader = new(bins);

        Assert.Equal(expected, reader.ReadEndOfSliceFlag());
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

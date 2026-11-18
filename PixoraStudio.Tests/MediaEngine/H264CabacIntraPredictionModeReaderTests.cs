using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacIntraPredictionModeReaderTests
{
    [Fact]
    public void Read_ShouldDecodeFourCabac8x8ModeEntries()
    {
        ScriptedCabacReader bins = new(new[] { 0, 1, 0, 1, 1, 0, 0, 1, 0, 1 });
        H264CabacSyntaxElementReader syntax = new(bins);

        var result = new H264IntraPredictionModeReader().Read(syntax, true);

        Assert.True(result.Uses8X8Transform);
        Assert.Equal(4, result.Modes.Count);
        Assert.False(result.Modes[0].PreviousModeFlag);
        Assert.Equal(5u, result.Modes[0].RemainderMode);
        Assert.True(result.Modes[1].PreviousModeFlag);
        Assert.Null(result.Modes[1].RemainderMode);
        Assert.False(result.Modes[2].PreviousModeFlag);
        Assert.Equal(2u, result.Modes[2].RemainderMode);
        Assert.True(result.Modes[3].PreviousModeFlag);
        Assert.Null(result.Modes[3].RemainderMode);
        Assert.Equal(
            new[] { 68, 69, 69, 69, 68, 68, 69, 69, 69, 68 },
            bins.ContextIndices);
    }

    [Fact]
    public void Read_ShouldDecodeSixteenCabac4x4ModeEntries()
    {
        ScriptedCabacReader bins = new(Enumerable.Repeat(1, 16));
        H264CabacSyntaxElementReader syntax = new(bins);

        var result = new H264IntraPredictionModeReader().Read(syntax, false);

        Assert.False(result.Uses8X8Transform);
        Assert.Equal(16, result.Modes.Count);
        Assert.All(result.Modes, mode =>
        {
            Assert.True(mode.PreviousModeFlag);
            Assert.Null(mode.RemainderMode);
        });
        Assert.Equal(Enumerable.Repeat(68, 16), bins.ContextIndices);
    }

    private sealed class ScriptedCabacReader : IH264CabacBinReader
    {
        private readonly Queue<int> _bins;

        public ScriptedCabacReader(params int[] bins) : this((IEnumerable<int>)bins) { }

        public ScriptedCabacReader(IEnumerable<int> bins) => _bins = new Queue<int>(bins);

        public List<int> ContextIndices { get; } = [];

        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _bins.Dequeue();
        }

        public int DecodeBypass() => _bins.Dequeue();
        public int DecodeTerminate() => _bins.Dequeue();
    }
}

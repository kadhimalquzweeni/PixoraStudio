using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacMacroblockHeaderReaderTests
{
    [Fact]
    public void ReadIntraSlice_ShouldCreateIntraNxNHeader()
    {
        H264CabacSyntaxElementReader syntax = new(new ScriptedCabacReader(0));
        H264CabacMacroblockHeaderReader reader = new();

        var header = reader.ReadIntraSlice(syntax, 17, 1);

        Assert.Equal((uint)17, header.MacroblockAddress);
        Assert.Equal((uint)0, header.MbTypeCode);
        Assert.True(header.IsIntra);
        Assert.False(header.IsIntra16x16);
        Assert.False(header.IsPcm);
    }

    [Fact]
    public void ReadIntraSlice_ShouldMapI16x16HeaderFields()
    {
        H264CabacSyntaxElementReader syntax = new(new ScriptedCabacReader(
            new[] { 1, 0, 0, 0, 0, 0, 0 }));
        H264CabacMacroblockHeaderReader reader = new();

        var header = reader.ReadIntraSlice(syntax, 4, 0);

        Assert.Equal((uint)1, header.MbTypeCode);
        Assert.True(header.IsIntra16x16);
        Assert.Equal((uint)0, header.Intra16x16PredMode);
        Assert.Equal((uint)0, header.CodedBlockPatternLuma);
        Assert.Equal((uint)0, header.CodedBlockPatternChroma);
    }

    [Fact]
    public void ReadIntraSlice_ShouldCreatePcmHeader()
    {
        H264CabacSyntaxElementReader syntax = new(new ScriptedCabacReader(new[] { 1, 1 }));
        H264CabacMacroblockHeaderReader reader = new();

        var header = reader.ReadIntraSlice(syntax, 0, 0);

        Assert.True(header.IsIntra);
        Assert.True(header.IsPcm);
        Assert.False(header.IsIntra16x16);
    }

    private sealed class ScriptedCabacReader : IH264CabacBinReader
    {
        private readonly Queue<int> _bins;

        public ScriptedCabacReader(params int[] bins) => _bins = new Queue<int>(bins);

        public ScriptedCabacReader(IEnumerable<int> bins) => _bins = new Queue<int>(bins);

        public int DecodeBin(int contextIndex) => _bins.Dequeue();
        public int DecodeBypass() => _bins.Dequeue();
        public int DecodeTerminate() => _bins.Dequeue();
    }
}

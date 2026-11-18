using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264CabacIntraMacroblockSyntaxReaderTests
{
    [Fact]
    public void Read_ShouldParseCabacIntraNxNSyntax()
    {
        int[] bins =
        [
            0, // mb_type: I_NxN
            1, // transform_size_8x8_flag
            .. Enumerable.Repeat(1, 16), // previous intra prediction modes
            0, // intra_chroma_pred_mode
            1, 0, 1, 1, // luma coded block pattern
            1, 0, // chroma coded block pattern
            1, 0 // mb_qp_delta = +1
        ];
        ScriptedCabacReader binReader = new(bins);
        H264CabacSyntaxElementReader syntax = new(binReader);
        H264CabacMacroblockHeaderReader headerReader = new();
        H264MacroblockHeader header = headerReader.ReadIntraSlice(syntax, 0, 0);

        H264CabacIntraMacroblockSyntaxReader reader = new();
        H264IntraMacroblockSyntax result = reader.Read(
            syntax, header, CreateSps(), CreatePps(transform8x8: true),
            chromaPredictionContextIncrement: 0,
            lumaCbpContextIncrements: new[] { 0, 1, 2, 3 },
            chromaCbpContextIncrements: new[] { 1, 2 },
            qpDeltaContextIncrement: 1);

        Assert.True(result.TransformSize8X8Flag);
        Assert.Equal(4, result.LumaPredictionModes!.Modes.Count);
        Assert.Equal((uint)0, result.IntraChromaPredictionMode);
        Assert.Equal((uint)13, result.CodedBlockPattern.Luma);
        Assert.Equal((uint)1, result.CodedBlockPattern.Chroma);
        Assert.Equal(1, result.MacroblockQpDelta);
    }

    [Fact]
    public void Read_ShouldParseCabacIntra16x16Syntax()
    {
        ScriptedCabacReader binReader = new(new[] { 1, 0, 0, 0, 0, 0, 0, 0, 1, 0 });
        H264CabacSyntaxElementReader syntax = new(binReader);
        H264MacroblockHeader header = new H264CabacMacroblockHeaderReader()
            .ReadIntraSlice(syntax, 0, 0);

        H264IntraMacroblockSyntax result = new H264CabacIntraMacroblockSyntaxReader().Read(
            syntax, header, CreateSps(), CreatePps(transform8x8: false),
            chromaPredictionContextIncrement: 0,
            lumaCbpContextIncrements: null,
            chromaCbpContextIncrements: null,
            qpDeltaContextIncrement: 0);

        Assert.True(header.IsIntra16x16);
        Assert.Null(result.TransformSize8X8Flag);
        Assert.Null(result.LumaPredictionModes);
        Assert.Equal((uint)0, result.IntraChromaPredictionMode);
        Assert.Equal(1, result.MacroblockQpDelta);
    }

    [Fact]
    public void Read_ShouldRejectCavlcPps()
    {
        ScriptedCabacReader binReader = new(0);
        H264CabacSyntaxElementReader syntax = new(binReader);
        H264MacroblockHeader header = new H264CabacMacroblockHeaderReader()
            .ReadIntraSlice(syntax, 0, 0);

        Assert.Throws<ArgumentException>(() =>
            new H264CabacIntraMacroblockSyntaxReader().Read(
                syntax, header, CreateSps(), CreatePps(entropyCoding: false),
                0, new[] { 0, 0, 0, 0 }, new[] { 0, 0 }, 0));
    }

    private static H264SequenceParameterSet CreateSps() => new(
        0, 100, 42, 1, false, 0, 0, 0, 2, 0, 1, 1, 1,
        true, true, 32, 32, false);

    private static H264PictureParameterSetHeader CreatePps(
        bool transform8x8, bool entropyCoding = true) =>
        new(0, 0, entropyCoding, false, 0)
        {
            Transform8X8ModeFlag = transform8x8,
            PicInitQpMinus26 = 0
        };

    private sealed class ScriptedCabacReader : IH264CabacBinReader
    {
        private readonly Queue<int> _bins;
        public ScriptedCabacReader(params int[] bins) => _bins = new Queue<int>(bins);
        public int DecodeBin(int contextIndex) => _bins.Dequeue();
        public int DecodeBypass() => _bins.Dequeue();
        public int DecodeTerminate() => _bins.Dequeue();
    }
}

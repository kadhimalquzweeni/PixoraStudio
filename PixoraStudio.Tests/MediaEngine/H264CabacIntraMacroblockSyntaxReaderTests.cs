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
            .. Enumerable.Repeat(1, 4), // previous intra prediction modes
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
    public void Read_ShouldParseSixteenCabac4x4PredictionModesWithoutTransformFlag()
    {
        int[] bins =
        [
            0, // mb_type: I_NxN
            .. Enumerable.Repeat(1, 16), // previous intra prediction modes
            0, // intra_chroma_pred_mode
            0, 0, 0, 0, // luma coded block pattern
            0 // chroma coded block pattern
        ];
        ScriptedCabacReader binReader = new(bins);
        H264CabacSyntaxElementReader syntax = new(binReader);
        H264MacroblockHeader header = new H264CabacMacroblockHeaderReader()
            .ReadIntraSlice(syntax, 0, 0);

        H264IntraMacroblockSyntax result = new H264CabacIntraMacroblockSyntaxReader().Read(
            syntax, header, CreateSps(), CreatePps(transform8x8: false),
            chromaPredictionContextIncrement: 0,
            lumaCbpContextIncrements: new[] { 0, 0, 0, 0 },
            chromaCbpContextIncrements: new[] { 0, 0 },
            qpDeltaContextIncrement: 0);

        Assert.Null(result.TransformSize8X8Flag);
        Assert.Equal(16, result.LumaPredictionModes!.Modes.Count);
        Assert.Null(result.MacroblockQpDelta);
    }

    [Fact]
    public void Read_ShouldParseCabacIntra16x16Syntax()
    {
        ScriptedCabacReader binReader = new(new[] { 1, 0, 0, 0, 0, 0, 0, 1, 0 });
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
                syntax, header, CreateSps(), CreatePps(false, entropyCoding: false),
                0, new[] { 0, 0, 0, 0 }, new[] { 0, 0 }, 0));
    }


    [Fact]
    public void Read_ShouldDeriveAllNeighbourContextsFromPictureState()
    {
        H264CabacIntraMacroblockNeighbourContext neighbours = new();
        neighbours.RecordMacroblock(
            2, 0, 1, 7, false, true, false, 15, 2, 1, 2);
        neighbours.RecordMacroblock(
            1, 1, 0, 7, true, false, false, 0, 0, 0, 0);

        int[] bins =
        [
            0, // I_NxN mb_type
            .. Enumerable.Repeat(1, 16), // sixteen previous intra modes
            0, // chroma prediction mode
            1, 0, 0, 0, // luma CBP
            0, // chroma CBP
            0 // mb_qp_delta
        ];
        ScriptedCabacReader binReader = new(bins);
        H264CabacSyntaxElementReader syntax = new(binReader);
        H264MacroblockHeader header = new H264CabacMacroblockHeaderReader()
            .ReadIntraSlice(syntax, 3, 1);

        H264IntraMacroblockSyntax result =
            new H264CabacIntraMacroblockSyntaxReader().Read(
                syntax, header, CreateSps(), CreatePps(transform8x8: false),
                neighbours, 1, 1, 7, previousMacroblockAddress: 2);

        Assert.Equal((uint)1, result.CodedBlockPattern.Luma);
        Assert.Equal((uint)0, result.CodedBlockPattern.Chroma);
        Assert.Equal(new[] { 4 }, binReader.ContextIndices.Take(1));
        Assert.Equal(65, binReader.ContextIndices[17]);
        Assert.Equal(new[] { 75, 75, 73, 76, 78, 61 },
            binReader.ContextIndices.Skip(18));
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

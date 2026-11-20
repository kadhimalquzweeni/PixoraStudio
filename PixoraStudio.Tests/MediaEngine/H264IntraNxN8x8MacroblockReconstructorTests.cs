using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraNxN8x8MacroblockReconstructorTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void ReconstructIntraNxN8x8_ShouldCommitAllPlanes(
        uint chromaFormat, int chromaHeight)
    {
        H264SequenceParameterSet sps = CreateSps(chromaFormat);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);

        reconstructor.ReconstructIntraNxN8x8(
            0, 0, 0, CreateHeader(0), CreateSyntax(chromaFormat),
            CreateResidual(0, chromaFormat), 24, sps, CreatePps());

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        byte[] luma = surface.CopyLumaPlane();
        byte[] cb = surface.CopyCbPlane();
        byte[] cr = surface.CopyCrPlane();
        for (int y = 0; y < 16; y++)
            Assert.All(luma.Skip(y * surface.Width).Take(16),
                value => Assert.Equal((byte)128, value));
        for (int y = 0; y < chromaHeight; y++)
        {
            Assert.All(cb.Skip(y * surface.ChromaWidth).Take(8),
                value => Assert.Equal((byte)128, value));
            Assert.All(cr.Skip(y * surface.ChromaWidth).Take(8),
                value => Assert.Equal((byte)128, value));
        }
    }

    [Fact]
    public void ReconstructIntraNxN8x8_ShouldSupportCabacResidualPayloads()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264PictureParameterSetHeader pps =
            CreatePps() with { EntropyCodingModeFlag = true };

        new H264IntraMacroblockReconstructor(surface).ReconstructIntraNxN8x8(
            0, 0, 0, CreateHeader(0), CreateSyntax(1),
            CreateResidual(0, 1), 24, sps, pps);

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.All(surface.CopyLumaPlane(),
            value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCbPlane(),
            value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCrPlane(),
            value => Assert.Equal((byte)128, value));
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void ReconstructIntraNxN8x8_ShouldUseExternalTopReferences(
        uint chromaFormat, int chromaHeight)
    {
        H264SequenceParameterSet sps = CreateSps(chromaFormat);
        H264DecodedPictureSurface surface = new(sps);
        int chromaSamples = 8 * chromaHeight;
        surface.WriteIntraMacroblock(0, 0, 4,
            Enumerable.Repeat((byte)60, 256).ToArray(),
            Enumerable.Repeat((byte)50, chromaSamples).ToArray(),
            Enumerable.Repeat((byte)55, chromaSamples).ToArray());
        H264IntraMacroblockReconstructor reconstructor = new(surface);

        H264IntraMacroblockSyntax syntax = CreateSyntax(chromaFormat) with
        {
            IntraChromaPredictionMode = 2,
            LumaPredictionModes = new H264IntraPredictionModeSyntax(true,
                Enumerable.Range(0, 4)
                    .Select(_ => new H264IntraPredictionModeEntry(false, 0))
                    .ToArray())
        };

        reconstructor.ReconstructIntraNxN8x8(
            0, 1, 4, CreateHeader(2), syntax,
            CreateResidual(2, chromaFormat), 24, sps, CreatePps());

        for (int y = 16; y < 32; y++)
            Assert.All(surface.CopyLumaPlane().Skip(y * surface.Width).Take(16),
                value => Assert.Equal((byte)60, value));
        for (int y = 0; y < chromaHeight; y++)
        {
            Assert.All(surface.CopyCbPlane().Skip((y + chromaHeight) * surface.ChromaWidth).Take(8),
                value => Assert.Equal((byte)50, value));
            Assert.All(surface.CopyCrPlane().Skip((y + chromaHeight) * surface.ChromaWidth).Take(8),
                value => Assert.Equal((byte)55, value));
        }
    }

    [Fact]
    public void ReconstructIntraNxN8x8_ShouldPublishModesToFourByFourNeighbourContext()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264PictureParameterSetHeader pps = CreatePps();
        H264Intra8x8PredictionContext mode8 = new();
        H264Intra4x4PredictionContext mode4 = new();
        H264Intra8x8MacroblockLumaReconstructor reconstructor = new();
        H264IntraPredictionModeSyntax modes = new(true,
            Enumerable.Range(0, 4)
                .Select(_ => new H264IntraPredictionModeEntry(true, null))
                .ToArray());
        H264Intra8x8LumaResidualBlock[] residual =
        [
            new(0, 0, false, new int[64]),
            new(1, 0, false, new int[64]),
            new(0, 1, false, new int[64]),
            new(1, 1, false, new int[64])
        ];

        reconstructor.Reconstruct(
            0, 0, 3, new byte[16], new byte[16], 0,
            false, false, false, new byte[8], false,
            modes, residual, mode8, mode4, 24, sps, pps);

        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            Assert.Equal((uint?)2, mode4.GetRecordedMode(x, y, 3));
    }

    [Fact]
    public void ReconstructIntraNxN8x8_ShouldUseAvailableAboveRightMacroblockSamples()
    {
        H264SequenceParameterSet sps = CreateSps(1) with
        {
            PicWidthInMbsMinus1 = 2
        };
        H264DecodedPictureSurface surface = new(sps);
        int chromaSamples = 64;
        surface.WriteIntraMacroblock(1, 0, 6,
            Enumerable.Repeat((byte)60, 256).ToArray(),
            Enumerable.Repeat((byte)50, chromaSamples).ToArray(),
            Enumerable.Repeat((byte)55, chromaSamples).ToArray());
        surface.WriteIntraMacroblock(2, 0, 6,
            Enumerable.Repeat((byte)80, 256).ToArray(),
            Enumerable.Repeat((byte)70, chromaSamples).ToArray(),
            Enumerable.Repeat((byte)75, chromaSamples).ToArray());

        H264IntraMacroblockSyntax syntax = CreateSyntax(1) with
        {
            IntraChromaPredictionMode = 0,
            LumaPredictionModes = new H264IntraPredictionModeSyntax(true,
            [
                new H264IntraPredictionModeEntry(false, 0),
                new H264IntraPredictionModeEntry(false, 2),
                new H264IntraPredictionModeEntry(true, null),
                new H264IntraPredictionModeEntry(true, null)
            ])
        };

        new H264IntraMacroblockReconstructor(surface).ReconstructIntraNxN8x8(
            1, 1, 6, CreateHeader(4), syntax,
            CreateResidual(4, 1), 24, sps, CreatePps());

        // The top-right block's diagonal-down-left prediction reaches the
        // above-right macroblock. Without those samples this value is 60.
        Assert.Equal((byte)74, surface.GetLumaSample(31, 16));
    }

    [Fact]
    public void ReconstructIntraNxN8x8_ShouldRejectMissingOrMalformedResidualGrid()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);
        H264IntraLumaResidualMacroblock residual = CreateResidual(0, 1);

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructIntraNxN8x8(
                0, 0, 0, CreateHeader(0),
                CreateSyntax(1) with { TransformSize8X8Flag = false },
                residual, 24, sps, CreatePps()));

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructIntraNxN8x8(
                0, 0, 0, CreateHeader(0), CreateSyntax(1),
                residual with { Intra8x8Blocks = residual.Intra8x8Blocks.Take(3).ToArray() },
                24, sps, CreatePps()));

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructIntraNxN8x8(
                0, 0, 0, CreateHeader(0), CreateSyntax(1) with
                { CodedBlockPattern = new H264CodedBlockPattern(0, 1, 0, 1) },
                residual, 24, sps, CreatePps()));

        Assert.False(surface.IsMacroblockDecoded(0, 0));
    }

    private static H264MacroblockHeader CreateHeader(uint address) =>
        new(address, 0, false, 0, true, false, null, null, null, false);

    private static H264IntraMacroblockSyntax CreateSyntax(uint chromaFormat) =>
        new(true,
            new H264IntraPredictionModeSyntax(true,
                Enumerable.Range(0, 4)
                    .Select(_ => new H264IntraPredictionModeEntry(true, null))
                    .ToArray()),
            0, new H264CodedBlockPattern(0, 0, 0, chromaFormat), null);

    private static H264IntraLumaResidualMacroblock CreateResidual(
        uint address, uint chromaFormat)
    {
        int chromaHeight = chromaFormat == 1 ? 8 : 16;
        int blockRows = chromaHeight / 4;
        int chromaBlockCount = 2 * blockRows;
        return new H264IntraLumaResidualMacroblock(address, null, [])
        {
            Intra8x8Blocks =
            [
                new(0, 0, false, new int[64]),
                new(1, 0, false, new int[64]),
                new(0, 1, false, new int[64]),
                new(1, 1, false, new int[64])
            ],
            ChromaComponents =
            [
                CreateChroma(1, chromaBlockCount),
                CreateChroma(2, chromaBlockCount)
            ]
        };
    }

    private static H264IntraChromaResidualComponent CreateChroma(
        byte componentId, int blockCount) =>
        new(componentId, new H264CavlcResidualBlock(blockCount, 0, 0, 0, new int[blockCount]),
            Enumerable.Range(0, blockCount)
                .Select(index => new H264IntraChromaResidualBlock(
                    index % 2, index / 2, false,
                    new H264CavlcResidualBlock(15, 0, 0, 0, new int[15])))
                .ToArray());

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) =>
        new(0, 100, 40, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0) { Transform8X8ModeFlag = true };
}

using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraNxNMacroblockReconstructorTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void ReconstructIntraNxN4x4_ShouldCommitLumaAndChromaPlanes(
        uint chromaFormat, int chromaHeight)
    {
        H264SequenceParameterSet sps = CreateSps(chromaFormat);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);

        reconstructor.ReconstructIntraNxN4x4(
            0, 0, 3,
            CreateHeader(0),
            CreateSyntax(chromaFormat),
            CreateResidual(0, chromaFormat),
            24, sps, CreatePps());

        Assert.True(surface.IsMacroblockDecoded(0, 0));
        byte[] luma = surface.CopyLumaPlane();
        for (int y = 0; y < 16; y++)
            Assert.All(luma.Skip(y * surface.Width).Take(16),
                sample => Assert.Equal((byte)128, sample));

        byte[] cb = surface.CopyCbPlane();
        byte[] cr = surface.CopyCrPlane();
        for (int y = 0; y < chromaHeight; y++)
        {
            Assert.All(cb.Skip(y * surface.ChromaWidth).Take(8),
                sample => Assert.Equal((byte)128, sample));
            Assert.All(cr.Skip(y * surface.ChromaWidth).Take(8),
                sample => Assert.Equal((byte)128, sample));
        }
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void ReconstructIntraNxN4x4_ShouldUseExternalMacroblockReferences(
        uint chromaFormat, int chromaHeight)
    {
        H264SequenceParameterSet sps = CreateSps(chromaFormat);
        H264DecodedPictureSurface surface = new(sps);
        int chromaSamples = 8 * chromaHeight;

        surface.WriteIntraMacroblock(0, 0, 4,
            Enumerable.Repeat((byte)60, 256).ToArray(),
            Enumerable.Repeat((byte)50, chromaSamples).ToArray(),
            Enumerable.Repeat((byte)55, chromaSamples).ToArray());
        surface.WriteIntraMacroblock(1, 0, 4,
            Enumerable.Repeat((byte)40, 256).ToArray(),
            Enumerable.Repeat((byte)30, chromaSamples).ToArray(),
            Enumerable.Repeat((byte)35, chromaSamples).ToArray());
        surface.WriteIntraMacroblock(0, 1, 4,
            Enumerable.Repeat((byte)80, 256).ToArray(),
            Enumerable.Repeat((byte)70, chromaSamples).ToArray(),
            Enumerable.Repeat((byte)75, chromaSamples).ToArray());

        H264IntraMacroblockReconstructor reconstructor = new(surface);
        reconstructor.ReconstructIntraNxN4x4(
            1, 1, 4,
            CreateHeader(3),
            CreateSyntax(chromaFormat, chromaMode: 2),
            CreateResidual(3, chromaFormat),
            24, sps, CreatePps());

        Assert.Equal((byte)60, surface.GetLumaSample(16, 16));
        Assert.Equal((byte)50, surface.GetChromaSample(1, 8, 8));
        Assert.Equal((byte)55, surface.GetChromaSample(2, 8, 8));
    }

    [Fact]
    public void ReconstructIntraNxN4x4_ShouldRejectIntra8x8AndMissingModes()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);
        H264IntraMacroblockSyntax validSyntax = CreateSyntax(1);
        H264IntraLumaResidualMacroblock residual = CreateResidual(0, 1);

        Assert.Throws<NotSupportedException>(() =>
            reconstructor.ReconstructIntraNxN4x4(
                0, 0, 0, CreateHeader(0),
                validSyntax with { TransformSize8X8Flag = true },
                residual, 24, sps, CreatePps() with { Transform8X8ModeFlag = true }));

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructIntraNxN4x4(
                0, 0, 0, CreateHeader(0),
                validSyntax with { LumaPredictionModes = null },
                residual, 24, sps, CreatePps()));

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructIntraNxN4x4(
                0, 0, 0, CreateHeader(0), validSyntax,
                residual, 24, sps, CreatePps() with { Transform8X8ModeFlag = true }));

        Assert.False(surface.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void ReconstructIntraNxN4x4_ShouldRejectIntra16x16DcAndDuplicateChroma()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);
        H264IntraLumaResidualMacroblock residual = CreateResidual(0, 1);

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructIntraNxN4x4(
                0, 0, 0, CreateHeader(0), CreateSyntax(1),
                residual with { Intra16x16Dc = CreateBlock(16) },
                24, sps, CreatePps()));

        Assert.Throws<FormatException>(() =>
            reconstructor.ReconstructIntraNxN4x4(
                0, 0, 0, CreateHeader(0), CreateSyntax(1),
                residual with
                {
                    ChromaComponents =
                    [
                        residual.ChromaComponents[0],
                        residual.ChromaComponents[0]
                    ]
                },
                24, sps, CreatePps()));

        Assert.False(surface.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void ReconstructIntraNxN4x4_ShouldRejectWrongMacroblockTypeAndCabac()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);
        H264IntraLumaResidualMacroblock residual = CreateResidual(0, 1);

        Assert.Throws<NotSupportedException>(() =>
            reconstructor.ReconstructIntraNxN4x4(
                0, 0, 0, CreateHeader(0) with { IsIntra16x16 = true },
                CreateSyntax(1), residual, 24, sps, CreatePps()));

        Assert.Throws<NotSupportedException>(() =>
            reconstructor.ReconstructIntraNxN4x4(
                0, 0, 0, CreateHeader(0), CreateSyntax(1),
                residual, 24, sps, CreatePps() with { EntropyCodingModeFlag = true }));
    }

    [Fact]
    public void ResetPictureContexts_ShouldAllowASeparatePictureToStartWithDcPrediction()
    {
        H264SequenceParameterSet sps = CreateSps(1);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraMacroblockReconstructor reconstructor = new(surface);

        reconstructor.ResetPictureContexts();

        reconstructor.ReconstructIntraNxN4x4(
            0, 0, 0, CreateHeader(0), CreateSyntax(1),
            CreateResidual(0, 1), 24, sps, CreatePps());

        Assert.True(surface.IsMacroblockDecoded(0, 0));
    }

    private static H264SequenceParameterSet CreateSps(uint chromaFormat) =>
        new(0, 66, 30, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);

    private static H264MacroblockHeader CreateHeader(uint address) =>
        new(address, 0, false, 0, true, false, null, null, null, false);

    private static H264IntraMacroblockSyntax CreateSyntax(
        uint chromaFormat, uint chromaMode = 0) =>
        new(null,
            new H264IntraPredictionModeSyntax(false,
                Enumerable.Range(0, 16)
                    .Select(_ => new H264IntraPredictionModeEntry(true, null))
                    .ToArray()),
            chromaMode,
            new H264CodedBlockPattern(0, 0, 0, chromaFormat),
            null);

    private static H264IntraLumaResidualMacroblock CreateResidual(
        uint address, uint chromaFormat)
    {
        int chromaHeight = chromaFormat == 1 ? 8 : 16;
        int chromaBlockRows = chromaHeight / 4;
        H264IntraLumaResidualBlock[] luma = Enumerable.Range(0, 16)
            .Select(index => new H264IntraLumaResidualBlock(
                new[] { 0, 1, 0, 1, 2, 3, 2, 3, 0, 1, 0, 1, 2, 3, 2, 3 }[index],
                new[] { 0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 3, 3, 2, 2, 3, 3 }[index],
                false, CreateBlock(16)))
            .ToArray();
        int chromaBlockCount = 2 * chromaBlockRows;
        H264IntraChromaResidualComponent[] chroma =
        [
            CreateChroma(1, chromaBlockCount),
            CreateChroma(2, chromaBlockCount)
        ];
        return new H264IntraLumaResidualMacroblock(address, null, luma)
        {
            ChromaComponents = chroma
        };
    }

    private static H264IntraChromaResidualComponent CreateChroma(
        byte componentId, int blockCount) =>
        new(componentId, CreateBlock(blockCount),
            Enumerable.Range(0, blockCount)
                .Select(index => new H264IntraChromaResidualBlock(
                    index % 2, index / 2, false, CreateBlock(15)))
                .ToArray());

    private static H264CavlcResidualBlock CreateBlock(int coefficientCount) =>
        new(coefficientCount, 0, 0, 0, new int[coefficientCount]);
}

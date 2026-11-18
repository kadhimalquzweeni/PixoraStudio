using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DeblockingPictureFilterTests
{
    [Fact]
    public void Apply_ShouldRequireCompletePictureAndDeblockingMetadata()
    {
        H264DecodedPictureSurface surface = Surface(1, 0, 1);

        Assert.Throws<InvalidOperationException>(() =>
            H264DeblockingPictureFilter.Apply(surface));

        WriteMacroblock(surface, 0, 0, 0, 1);
        WriteMacroblock(surface, 1, 0, 0, 1);
        surface.SetDeblockingMetadata(0, 0, Metadata(1, 4, transform8x8: true));

        Assert.Throws<InvalidOperationException>(() =>
            H264DeblockingPictureFilter.Apply(surface));
    }

    [Fact]
    public void Apply_ShouldFilterExternalIntraBoundaryAndBecomeOneTimeOperation()
    {
        H264DecodedPictureSurface surface = CreateTwoMacroblockEdge(
            leftSliceId: 0, rightSliceId: 0,
            leftDisableIdc: 0, rightDisableIdc: 0);

        int filtered = H264DeblockingPictureFilter.Apply(surface);

        Assert.True(filtered > 0);
        Assert.Equal((byte)111, surface.GetLumaSample(15, 0));
        Assert.Equal((byte)111, surface.GetLumaSample(16, 0));
        Assert.True(surface.IsDeblockingFilterApplied);
        Assert.Throws<InvalidOperationException>(() =>
            H264DeblockingPictureFilter.Apply(surface));
        Assert.Throws<InvalidOperationException>(() =>
            surface.WriteMacroblock(0, 0, 0, true, new byte[256], new byte[64], new byte[64]));
        Assert.Throws<InvalidOperationException>(() =>
            surface.SetDeblockingMetadata(0, 0, Metadata(0, 4, transform8x8: true)));
    }

    [Fact]
    public void Apply_WithDisableIdcTwoAcrossSliceBoundary_ShouldSkipExternalEdge()
    {
        H264DecodedPictureSurface surface = CreateTwoMacroblockEdge(
            leftSliceId: 0, rightSliceId: 1,
            leftDisableIdc: 2, rightDisableIdc: 0);

        H264DeblockingPictureFilter.Apply(surface);

        Assert.Equal((byte)110, surface.GetLumaSample(15, 0));
        Assert.Equal((byte)112, surface.GetLumaSample(16, 0));
    }

    [Fact]
    public void Apply_WithDisableIdcOneOnEitherSide_ShouldSkipExternalEdge()
    {
        H264DecodedPictureSurface surface = CreateTwoMacroblockEdge(
            leftSliceId: 0, rightSliceId: 0,
            leftDisableIdc: 1, rightDisableIdc: 0);

        H264DeblockingPictureFilter.Apply(surface);

        Assert.Equal((byte)110, surface.GetLumaSample(15, 0));
        Assert.Equal((byte)112, surface.GetLumaSample(16, 0));
    }

    [Fact]
    public void Apply_ShouldTraverseFourTwoTwoChromaGeometry()
    {
        H264DecodedPictureSurface surface = Surface(0, 1, 2);
        for (int y = 0; y < 2; y++)
        {
            WriteMacroblock(surface, 0, y, 0, 2);
            surface.SetDeblockingMetadata(0, y, Metadata(0, 8, transform8x8: false));
        }

        H264DeblockingPictureFilter.Apply(surface);

        Assert.True(surface.IsDeblockingFilterApplied);
        Assert.Equal((byte)0, surface.GetChromaSample(1, 0, 15));
        Assert.Equal((byte)0, surface.GetChromaSample(2, 7, 16));
    }

    [Fact]
    public void Apply_ShouldCompleteAllVerticalEdgesBeforeHorizontalEdges()
    {
        H264DecodedPictureSurface surface = Surface(0, 0, 1);
        byte[] source = new byte[256];
        for (int y = 0; y < 16; y++)
        for (int x = 0; x < 16; x++)
            source[y * 16 + x] = (byte)(96 + ((x * 13 + y * 7) % 24));

        WriteMacroblock(surface, 0, 0, 0, 1, source);
        surface.SetDeblockingMetadata(0, 0, Metadata(0, 4, transform8x8: false));

        byte[] expected = (byte[])source.Clone();
        H264DeblockingEdgeParameters parameters =
            new(3, 40, 40, 0, 0, false);

        // Normative picture traversal: all vertical edges first.
        for (int edge = 4; edge < 16; edge += 4)
        for (int blockRow = 0; blockRow < 4; blockRow++)
            H264DeblockingFilter.FilterEdge(
                expected, blockRow * 4 * 16 + edge, 1, 16, 4, parameters);

        // Then all horizontal edges, using the vertically filtered samples.
        for (int edge = 4; edge < 16; edge += 4)
        for (int blockColumn = 0; blockColumn < 4; blockColumn++)
            H264DeblockingFilter.FilterEdge(
                expected, edge * 16 + blockColumn * 4, 16, 1, 4, parameters);

        H264DeblockingPictureFilter.Apply(surface);

        Assert.Equal(expected, surface.CopyLumaPlane());
    }

    [Fact]
    public void Apply_WithEightByEightTransform_ShouldSkipInternalFourByFourLumaEdges()
    {
        H264DecodedPictureSurface surface = Surface(0, 0, 1);
        byte[] luma = Enumerable.Repeat((byte)100, 256).ToArray();
        for (int y = 0; y < 16; y++)
        {
            luma[y * 16 + 0] = 108;
            luma[y * 16 + 1] = 109;
            luma[y * 16 + 2] = 110;
            luma[y * 16 + 3] = 110;
            luma[y * 16 + 4] = 112;
            luma[y * 16 + 5] = 112;
            luma[y * 16 + 6] = 113;
            luma[y * 16 + 7] = 114;
        }
        WriteMacroblock(surface, 0, 0, 0, 1, luma);
        surface.SetDeblockingMetadata(0, 0, Metadata(0, 4, transform8x8: true));

        H264DeblockingPictureFilter.Apply(surface);

        Assert.Equal((byte)110, surface.GetLumaSample(3, 0));
        Assert.Equal((byte)112, surface.GetLumaSample(4, 0));
    }

    private static H264DecodedPictureSurface CreateTwoMacroblockEdge(
        int leftSliceId,
        int rightSliceId,
        int leftDisableIdc,
        int rightDisableIdc)
    {
        H264DecodedPictureSurface surface = Surface(1, 0, 1);
        byte[] left = Enumerable.Repeat((byte)100, 256).ToArray();
        byte[] right = Enumerable.Repeat((byte)100, 256).ToArray();
        for (int y = 0; y < 16; y++)
        {
            left[y * 16 + 12] = 108;
            left[y * 16 + 13] = 109;
            left[y * 16 + 14] = 110;
            left[y * 16 + 15] = 110;
            right[y * 16 + 0] = 112;
            right[y * 16 + 1] = 112;
            right[y * 16 + 2] = 113;
            right[y * 16 + 3] = 114;
        }

        WriteMacroblock(surface, 0, 0, leftSliceId, 1, left);
        WriteMacroblock(surface, 1, 0, rightSliceId, 1, right);
        surface.SetDeblockingMetadata(0, 0,
            Metadata(leftDisableIdc, 4, transform8x8: true));
        surface.SetDeblockingMetadata(1, 0,
            Metadata(rightDisableIdc, 4, transform8x8: true));
        return surface;
    }

    private static H264DecodedPictureSurface Surface(
        uint widthMinus1, uint heightMinus1, uint chromaFormat) =>
        new(new H264SequenceParameterSet(
            0, 66, 30, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            widthMinus1, heightMinus1, true, true,
            (widthMinus1 + 1) * 16, (heightMinus1 + 1) * 16, false));

    private static void WriteMacroblock(
        H264DecodedPictureSurface surface,
        int x,
        int y,
        int sliceId,
        uint chromaFormat,
        byte[]? luma = null)
    {
        int chromaSamples = chromaFormat == 1 ? 64 : 128;
        surface.WriteMacroblock(x, y, sliceId, false,
            luma ?? new byte[256], new byte[chromaSamples], new byte[chromaSamples]);
    }

    private static H264DeblockingMacroblockMetadata Metadata(
        int disableIdc,
        int chromaBlockCount,
        bool transform8x8) =>
        new(40, 40, 40, disableIdc, 0, 0, transform8x8,
            Enumerable.Repeat(IntraBlock(), 16).ToArray(),
            Enumerable.Repeat(IntraBlock(), chromaBlockCount).ToArray(),
            Enumerable.Repeat(IntraBlock(), chromaBlockCount).ToArray());

    private static H264DeblockingBlockMetadata IntraBlock() =>
        new(true, false, null, null, null, null);
}

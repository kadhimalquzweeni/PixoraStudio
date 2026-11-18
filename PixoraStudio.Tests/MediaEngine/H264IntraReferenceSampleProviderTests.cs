using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraReferenceSampleProviderTests
{
    [Theory]
    [InlineData(1, 8)]
    [InlineData(2, 16)]
    public void GatherChroma_ShouldReadTopLeftAndLeftFromDecodedNeighbourMacroblocks(
        uint format, int height)
    {
        var surface = new H264DecodedPictureSurface(CreateSps(format));
        int count = 8 * height;
        surface.WriteIntraMacroblock(0, 0, 3,
            Enumerable.Repeat((byte)10, 256).ToArray(),
            Enumerable.Repeat((byte)20, count).ToArray(),
            Enumerable.Repeat((byte)30, count).ToArray());
        surface.WriteIntraMacroblock(1, 0, 3,
            Enumerable.Repeat((byte)11, 256).ToArray(),
            Enumerable.Repeat((byte)21, count).ToArray(),
            Enumerable.Repeat((byte)31, count).ToArray());
        surface.WriteIntraMacroblock(0, 1, 3,
            Enumerable.Repeat((byte)12, 256).ToArray(),
            Enumerable.Repeat((byte)22, count).ToArray(),
            Enumerable.Repeat((byte)32, count).ToArray());

        var provider = new H264IntraReferenceSampleProvider(surface);
        var refs = provider.GatherChroma(1, 1, 1, 3, false);

        Assert.True(refs.HasTop);
        Assert.True(refs.HasLeft);
        Assert.True(refs.HasTopLeft);
        Assert.All(refs.Top, value => Assert.Equal((byte)21, value));
        Assert.All(refs.Left, value => Assert.Equal((byte)22, value));
        Assert.Equal((byte)20, refs.TopLeft);
    }

    [Fact]
    public void GatherLuma_ShouldMarkPictureEdgesUnavailable()
    {
        var surface = new H264DecodedPictureSurface(CreateSps(1));
        var refs = new H264IntraReferenceSampleProvider(surface)
            .GatherLuma16x16(0, 0, 0, false);

        Assert.False(refs.HasTop);
        Assert.False(refs.HasLeft);
        Assert.False(refs.HasTopLeft);
        Assert.All(refs.Top, value => Assert.Equal((byte)0, value));
        Assert.All(refs.Left, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void GatherLuma_ShouldRespectSliceAndConstrainedIntraAvailability()
    {
        var surface = new H264DecodedPictureSurface(CreateSps(1));
        surface.WriteIntraMacroblock(0, 0, 4, new byte[256],
            new byte[64], new byte[64]);
        surface.WriteIntraMacroblock(1, 0, 5, new byte[256],
            new byte[64], new byte[64]);
        var provider = new H264IntraReferenceSampleProvider(surface);

        var otherSlice = provider.GatherLuma16x16(1, 0, 4, false);
        Assert.False(otherSlice.HasLeft);

        // A decoded inter macroblock cannot be represented as intra through
        // this surface API; constrained-intra therefore retains only intra
        // macroblocks that have been committed by WriteIntraMacroblock.
        var constrained = provider.GatherLuma16x16(1, 0, 5, true);
        Assert.False(constrained.HasLeft);
    }

    [Fact]
    public void Gather_ShouldRejectCoordinatesOutsidePicture()
    {
        var provider = new H264IntraReferenceSampleProvider(
            new H264DecodedPictureSurface(CreateSps(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            provider.GatherLuma16x16(2, 0, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            provider.GatherChroma(3, 0, 0, 0, false));
    }

    private static H264SequenceParameterSet CreateSps(uint format) =>
        new(0, 66, 30, format, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);
}

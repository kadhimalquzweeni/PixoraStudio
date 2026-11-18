using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DecodedPictureBufferTests
{
    [Fact]
    public void StoreDecodedPicture_ShouldApplySlidingWindowAndKeepNewestShortTermReferences()
    {
        H264DecodedPictureBuffer dpb = new();
        for (int frame = 0; frame < 3; frame++)
            dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(frame, frame * 2),
                true, 2, frame, 16, Adaptive(false));

        Assert.Equal(new[] { 1, 2 }, dpb.Entries.Select(entry => entry.Picture.FrameNum));
        Assert.All(dpb.Entries, entry => Assert.False(entry.Picture.IsLongTerm));
    }

    [Fact]
    public void StoreDecodedPicture_IdrShouldClearPriorReferencesAndOptionallyMarkLongTerm()
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(1, 2),
            true, 4, 1, 16, Adaptive(false));
        H264DecodedPictureBufferEntry entry = dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(0, 0), true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, true, false, []), idrPicture: true)!;

        Assert.Single(dpb.Entries);
        Assert.True(entry.Picture.IsLongTerm);
        Assert.Equal(0, entry.Picture.LongTermFrameIndex);
        Assert.Equal(0, dpb.MaxLongTermFrameIndex);
    }

    [Fact]
    public void StoreDecodedPicture_Mmco1ShouldRemoveSelectedShortTermPicture()
    {
        H264DecodedPictureBuffer dpb = new();
        for (int frame = 0; frame < 3; frame++)
            dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(frame, frame),
                true, 4, frame, 16, Adaptive(false));

        H264DecodedPictureBufferEntry current = dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(3, 3), true, 4, 3, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(1, 2, null, null, null)))!;

        Assert.Equal(new[] { 1, 2, 3 }, dpb.Entries.Select(entry => entry.Picture.FrameNum));
        Assert.Equal(3, current.Picture.FrameNum);
    }

    [Fact]
    public void StoreDecodedPicture_Mmco3ShouldConvertShortTermToLongTerm()
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(0, 0),
            true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, true, false, []), idrPicture: true);
        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(1, 2),
            true, 4, 1, 16, Adaptive(false));

        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(2, 4),
            true, 4, 2, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(3, 0, null, 0, null)));

        Assert.DoesNotContain(dpb.Entries, entry => !entry.Picture.IsLongTerm);
        H264DecodedPictureBufferEntry longTerm = Assert.Single(
            dpb.Entries.Where(entry => entry.Picture.IsLongTerm));
        Assert.Equal(1, longTerm.Picture.FrameNum);
        Assert.Equal(0, longTerm.Picture.LongTermFrameIndex);
    }

    [Fact]
    public void StoreDecodedPicture_Mmco2ShouldRemoveLongTermPicture()
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(0, 0),
            true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, true, false, []), idrPicture: true);

        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(1, 2),
            true, 4, 1, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(2, null, 0, null, null)));

        Assert.DoesNotContain(dpb.Entries, entry => entry.Picture.IsLongTerm);
        Assert.Contains(dpb.Entries, entry => entry.Picture.FrameNum == 1);
    }

    [Fact]
    public void StoreDecodedPicture_Mmco4ShouldLimitLongTermIndices()
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(0, 0),
            true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, true, false, []), idrPicture: true);

        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(1, 2),
            true, 4, 1, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(4, null, null, null, 0)));

        Assert.Empty(dpb.Entries);
        Assert.Null(dpb.MaxLongTermFrameIndex);
    }

    [Fact]
    public void StoreDecodedPicture_Mmco5ShouldClearReferencesAndResetCurrentFrameNum()
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(1, 2),
            true, 4, 1, 16, Adaptive(false));

        H264DecodedPictureBufferEntry current = dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(2, 4), true, 4, 2, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(5, null, null, null, null)))!;

        Assert.Single(dpb.Entries);
        Assert.Equal(0, current.Picture.FrameNum);
        Assert.Null(dpb.MaxLongTermFrameIndex);
    }

    [Fact]
    public void StoreDecodedPicture_Mmco6ShouldMarkCurrentPictureLongTerm()
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(Surface(), new H264ReferencePicture(0, 0),
            true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, true, false, []), idrPicture: true);

        H264DecodedPictureBufferEntry current = dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(1, 2), true, 4, 1, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(6, null, null, 0, null)))!;

        Assert.True(current.Picture.IsLongTerm);
        Assert.Equal(0, current.Picture.LongTermFrameIndex);
        Assert.Single(dpb.Entries);
    }

    [Fact]
    public void BuildList0_ShouldReturnSurfacesInReferenceListOrder()
    {
        H264DecodedPictureBuffer dpb = new();
        H264DecodedPictureSurface first = Surface(10);
        H264DecodedPictureSurface second = Surface(20);
        dpb.StoreDecodedPicture(first, new H264ReferencePicture(1, 2),
            true, 4, 1, 16, Adaptive(false));
        dpb.StoreDecodedPicture(second, new H264ReferencePicture(2, 4),
            true, 4, 2, 16, Adaptive(false));

        IReadOnlyList<H264DecodedPictureSurface> list = dpb.BuildList0(3, 16, 2);

        Assert.Same(second, list[0]);
        Assert.Same(first, list[1]);
    }

    [Fact]
    public void StoreDecodedPicture_NonReferencePictureShouldNotEnterDpb()
    {
        H264DecodedPictureBuffer dpb = new();
        Assert.Null(dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(1, 2), false,
            4, 1, 16, null));
        Assert.Empty(dpb.Entries);
    }

    [Fact]
    public void StoreDecodedPicture_ShouldRejectUnavailableMmcoTargetsAndInvalidLongTermIndex()
    {
        H264DecodedPictureBuffer dpb = new();
        Assert.Throws<FormatException>(() => dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(1, 2), true, 4, 1, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(1, 0, null, null, null))));

        Assert.Throws<FormatException>(() => dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(1, 2), true, 4, 1, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(6, null, null, 0, null))));
    }

    [Fact]
    public void StoreDecodedPicture_ShouldRejectMissingMarkingAndDpbCapacityOverflow()
    {
        H264DecodedPictureBuffer dpb = new();
        Assert.Throws<ArgumentNullException>(() => dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(0, 0), true, 4, 0, 16, null));

        H264DecodedPictureBuffer full = new();
        full.StoreDecodedPicture(Surface(), new H264ReferencePicture(0, 0),
            true, 1, 0, 16, Adaptive(false));
        Assert.Throws<FormatException>(() => full.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(1, 2), true, 1, 1, 16,
            Adaptive(true)));
    }

    private static H264DecodedReferenceMarking Adaptive(
        bool adaptive, params H264MemoryManagementControlOperation[] operations) =>
        new(false, false, adaptive, operations);

    private static H264DecodedPictureSurface Surface(byte value = 128)
    {
        H264SequenceParameterSet sps = new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(0, 0, 0, false,
            Enumerable.Repeat(value, 256).ToArray(),
            Enumerable.Repeat(value, 64).ToArray(),
            Enumerable.Repeat(value, 64).ToArray());
        return surface;
    }
}

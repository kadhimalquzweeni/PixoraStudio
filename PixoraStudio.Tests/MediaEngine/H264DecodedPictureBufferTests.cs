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

        H264DecodedPictureBufferEntry longTerm = Assert.Single(
            dpb.Entries.Where(entry => entry.Picture.IsLongTerm));
        Assert.Equal(1, longTerm.Picture.FrameNum);
        Assert.Equal(0, longTerm.Picture.LongTermFrameIndex);
        Assert.Contains(dpb.Entries, entry =>
            !entry.Picture.IsLongTerm && entry.Picture.FrameNum == 2);
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

        Assert.Single(dpb.Entries);
        Assert.False(dpb.Entries[0].Picture.IsLongTerm);
        Assert.Equal(1, dpb.Entries[0].Picture.FrameNum);
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
    public void BuildBLists_ShouldReturnPocOrderedSurfacesForBothLists()
    {
        H264DecodedPictureBuffer dpb = new();
        H264DecodedPictureSurface pastNearest = Surface(80);
        H264DecodedPictureSurface pastOlder = Surface(60);
        H264DecodedPictureSurface future = Surface(100);
        dpb.StoreDecodedPicture(pastOlder, new H264ReferencePicture(1, 4),
            true, 4, 1, 16, Adaptive(false));
        dpb.StoreDecodedPicture(pastNearest, new H264ReferencePicture(2, 8),
            true, 4, 2, 16, Adaptive(false));
        dpb.StoreDecodedPicture(future, new H264ReferencePicture(3, 12),
            true, 4, 3, 16, Adaptive(false));

        var lists = dpb.BuildBLists(4, 16, 10, 3, 3);

        Assert.Same(pastNearest, lists.List0[0]);
        Assert.Same(pastOlder, lists.List0[1]);
        Assert.Same(future, lists.List0[2]);
        Assert.Same(future, lists.List1[0]);
        Assert.Same(pastNearest, lists.List1[1]);
        Assert.Same(pastOlder, lists.List1[2]);
    }

    [Fact]
    public void StoreDecodedPicture_ShouldApplyCompleteDeblockingMetadataBeforeReferenceInsertion()
    {
        H264DecodedPictureSurface surface = Surface();
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(
            surface, new H264ReferencePicture(0, 0), true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, false, false, []));

        Assert.True(surface.IsDeblockingFilterApplied);
        Assert.Same(surface, Assert.Single(dpb.Entries).Surface);
    }

    [Fact]
    public void StoreDecodedPicture_ShouldRejectCompletePixelsWithoutDeblockingMetadata()
    {
        H264DecodedPictureBuffer dpb = new();
        H264DecodedPictureSurface surface = Surface();

        Assert.Throws<InvalidOperationException>(() => dpb.StoreDecodedPicture(
            surface, new H264ReferencePicture(0, 0), true, 4, 0, 16,
            Adaptive(false)));
    }

    [Fact]
    public void StoreDecodedPicture_ShouldRejectIncompleteReferenceSurface()
    {
        H264DecodedPictureBuffer dpb = new();
        H264SequenceParameterSet sps = new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);

        Assert.Throws<InvalidOperationException>(() => dpb.StoreDecodedPicture(
            new H264DecodedPictureSurface(sps), new H264ReferencePicture(0, 0),
            true, 4, 0, 16, Adaptive(false)));
    }

    [Fact]
    public void CreateTemporalDirectMotionField_ShouldRestoreStoredCollocatedMotionAndReferenceLists()
    {
        H264DecodedPictureBuffer dpb = new();
        H264ReferencePicture collocated = new(1, 2);
        H264MotionVectorPictureState l0 = new(1, 1);
        H264MotionVectorPictureState l1 = new(1, 1);
        l0.RecordInterPartition(0, 0, 1, 1, 0, 0, new H264MotionVector(6, -4));
        H264ReferencePicture[] references0 = [new H264ReferencePicture(0, -2)];
        H264ReferencePicture[] references1 = [new H264ReferencePicture(2, 6)];

        dpb.StoreDecodedPicture(
            Surface(), collocated, true, 4, 1, 16, Adaptive(false),
            motionVectorStateL0: l0, motionVectorStateL1: l1,
            referenceList0: references0, referenceList1: references1);

        references0[0] = new H264ReferencePicture(0, 99);
        l0.RecordInterPartition(2, 0, 1, 1, 0, 0, new H264MotionVector(99, 99));
        H264DecodedPictureBufferEntry exposed = Assert.Single(dpb.Entries);
        exposed.MotionVectorStateL0!.RecordInterPartition(
            1, 0, 1, 1, 0, 0, new H264MotionVector(88, 88));

        H264TemporalDirectMotionField field = dpb.CreateTemporalDirectMotionField(collocated);
        Assert.Null(field.GetContext(1, 0, 1, 1, 10, [new H264ReferencePicture(0, -2)]));
        Assert.Null(field.GetContext(2, 0, 1, 1, 10, [new H264ReferencePicture(0, -2)]));
        H264TemporalDirectPredictionContext context = field
            .GetContext(0, 0, 1, 1, 10, [new H264ReferencePicture(0, -2)])!;

        Assert.Equal(new H264MotionVector(6, -4), context.CollocatedMotionVector);
        Assert.Equal(2, context.CollocatedPictureOrderCount);
        Assert.Equal(-2, context.CollocatedReferencePictureOrderCount);
        Assert.True(context.CollocatedPictureIsShortTerm);
    }

    [Fact]
    public void CreateTemporalDirectMotionField_ShouldRejectEntriesWithoutStoredMotionMetadata()
    {
        H264DecodedPictureBuffer dpb = new();
        H264ReferencePicture collocated = new(1, 2);
        dpb.StoreDecodedPicture(Surface(), collocated, true, 4, 1, 16, Adaptive(false));

        Assert.Throws<NotSupportedException>(() =>
            dpb.CreateTemporalDirectMotionField(collocated));
    }

    [Fact]
    public void StoreDecodedPicture_ShouldRejectPartialOrMismatchedTemporalMetadata()
    {
        H264DecodedPictureBuffer dpb = new();
        H264MotionVectorPictureState state = new(1, 1);
        Assert.Throws<ArgumentException>(() => dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(0, 0), true, 4, 0, 16,
            Adaptive(false), motionVectorStateL0: state));

        Assert.Throws<ArgumentException>(() => dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(0, 0), true, 4, 0, 16,
            Adaptive(false),
            motionVectorStateL0: new H264MotionVectorPictureState(2, 1),
            motionVectorStateL1: new H264MotionVectorPictureState(2, 1),
            referenceList0: [], referenceList1: []));
    }

    [Fact]
    public void Mmco3_ShouldPreserveStoredTemporalMotionMetadata()
    {
        H264DecodedPictureBuffer dpb = new();
        H264ReferencePicture collocated = new(1, 2);
        H264MotionVectorPictureState l0 = new(1, 1);
        H264MotionVectorPictureState l1 = new(1, 1);
        l0.RecordInterPartition(0, 0, 4, 4, 0, 0, new H264MotionVector(3, 4));
        dpb.StoreDecodedPicture(
            Surface(), collocated, true, 4, 1, 16, Adaptive(false),
            motionVectorStateL0: l0, motionVectorStateL1: l1,
            referenceList0: [new H264ReferencePicture(0, 0)],
            referenceList1: []);
        dpb.StoreDecodedPicture(
            Surface(), new H264ReferencePicture(2, 4), true, 4, 2, 16,
            Adaptive(true, new H264MemoryManagementControlOperation(3, 0, null, 0, null)));

        H264ReferencePicture converted = Assert.Single(
            dpb.Entries.Where(entry => entry.Picture.IsLongTerm)).Picture;
        H264TemporalDirectPredictionContext context = dpb
            .CreateTemporalDirectMotionField(converted)
            .GetContext(0, 0, 1, 1, 8, [new H264ReferencePicture(0, 0)])!;

        Assert.Equal(new H264MotionVector(3, 4), context.CollocatedMotionVector);
        Assert.True(context.CollocatedPictureIsShortTerm == false);
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
        H264DeblockingBlockMetadata intra =
            new(true, false, null, null, null, null);
        surface.SetDeblockingMetadata(0, 0,
            new H264DeblockingMacroblockMetadata(
                30, 30, 30, 0, 0, 0, true,
                Enumerable.Repeat(intra, 16).ToArray(),
                Enumerable.Repeat(intra, 4).ToArray(),
                Enumerable.Repeat(intra, 4).ToArray()));
        return surface;
    }
}

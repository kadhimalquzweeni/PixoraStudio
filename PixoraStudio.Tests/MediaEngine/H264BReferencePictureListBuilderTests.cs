using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BReferencePictureListBuilderTests
{
    private readonly H264BReferencePictureListBuilder _builder = new();

    [Fact]
    public void BuildLists_ShouldOrderPastAndFuturePicturesInOppositeDirections()
    {
        H264ReferencePicture[] references =
        [
            new(1, 4),
            new(2, 8),
            new(3, 12),
            new(4, 16),
            new(5, 20, true, 0)
        ];

        H264BReferencePictureLists lists = _builder.BuildLists(
            references, 6, 16, 10, 5, 5);

        Assert.Equal(new[] { 2, 1, 3, 4, 5 }, lists.List0.Select(p => p.FrameNum));
        Assert.Equal(new[] { 3, 4, 2, 1, 5 }, lists.List1.Select(p => p.FrameNum));
    }

    [Fact]
    public void BuildLists_ShouldPlaceEqualPocPicturesInTheRemainingList1Group()
    {
        H264ReferencePicture[] references =
        [
            new(1, 4),
            new(2, 10),
            new(3, 10),
            new(4, 12)
        ];

        H264BReferencePictureLists lists = _builder.BuildLists(
            references, 5, 16, 10, 4, 4);

        Assert.Equal(new[] { 1, 2, 3, 4 }, lists.List0.Select(picture => picture.FrameNum));
        Assert.Equal(new[] { 4, 2, 3, 1 }, lists.List1.Select(picture => picture.FrameNum));
    }

    [Fact]
    public void BuildLists_WhenDefaultListsAreIdentical_ShouldSwapFirstTwoList1Entries()
    {
        H264ReferencePicture[] references =
        [
            new(1, 4),
            new(2, 8)
        ];

        H264BReferencePictureLists lists = _builder.BuildLists(
            references, 3, 16, 10, 2, 2);

        Assert.Equal(new[] { 2, 1 }, lists.List0.Select(p => p.FrameNum));
        Assert.Equal(new[] { 1, 2 }, lists.List1.Select(p => p.FrameNum));
    }

    [Fact]
    public void BuildLists_ShouldApplyIndependentShortTermAndLongTermModifications()
    {
        H264ReferencePicture[] references =
        [
            new(1, 4),
            new(2, 8),
            new(3, 12),
            new(4, 16, true, 0)
        ];
        H264ReferenceListModification[] list0 =
        [
            new(0, 1, null), // CurrPicNum 5 -> PicNum 3
            new(3, null, null)
        ];
        H264ReferenceListModification[] list1 =
        [
            new(2, null, 0),
            new(3, null, null)
        ];

        H264BReferencePictureLists lists = _builder.BuildLists(
            references, 5, 16, 10, 3, 3, list0, list1);

        Assert.Equal(3, lists.List0[0].FrameNum);
        Assert.True(lists.List1[0].IsLongTerm);
        Assert.Equal(0, lists.List1[0].LongTermFrameIndex);
    }

    [Fact]
    public void BuildLists_ShouldRejectMissingReferencesAndMalformedModifications()
    {
        Assert.Throws<FormatException>(() => _builder.BuildLists(
            [new H264ReferencePicture(1, 4)], 2, 16, 10, 2, 1));
        Assert.Throws<FormatException>(() => _builder.BuildLists(
            [new H264ReferencePicture(1, 4)], 2, 16, 10, 1, 1,
            [new H264ReferenceListModification(0, 1, null)]));
        Assert.Throws<FormatException>(() => _builder.BuildLists(
            [new H264ReferencePicture(1, 4)], 2, 16, 10, 1, 1,
            [new H264ReferenceListModification(5, null, null), new(3, null, null)]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _builder.BuildLists([], -1, 16, 0, 1, 1));
    }
}

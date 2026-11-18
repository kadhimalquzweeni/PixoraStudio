using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264ReferencePictureListBuilderTests
{
    private readonly H264ReferencePictureListBuilder _builder = new();

    [Fact]
    public void BuildList0_ShouldOrderShortTermByDescendingPicNumThenLongTermAscending()
    {
        H264ReferencePicture[] references =
        [
            new(2, 20),
            new(7, 70, true, 1),
            new(5, 50),
            new(3, 30, true, 0),
            new(1, 10)
        ];

        IReadOnlyList<H264ReferencePicture> result =
            _builder.BuildList0(references, 6, 16, 5);

        Assert.Equal(
            new[] { 5, 2, 1, 3, 7 },
            result.Select(picture => picture.FrameNum));
    }

    [Fact]
    public void BuildList0_ShouldWrapFrameNumAboveCurrentFrameNum()
    {
        H264ReferencePicture[] references =
        [
            new(14, 140),
            new(3, 30),
            new(5, 50)
        ];

        IReadOnlyList<H264ReferencePicture> result =
            _builder.BuildList0(references, 2, 16, 3);

        Assert.Equal(new[] { 5, 3, 14 }, result.Select(picture => picture.FrameNum));
    }

    [Fact]
    public void BuildList0_ShouldApplySubtractShortTermModification()
    {
        H264ReferencePicture[] references =
        [
            new(4, 40),
            new(5, 50),
            new(6, 60)
        ];
        H264ReferenceListModification[] modifications =
        [
            new(0, 1, null), // CurrPicNum 7 -> PicNum 5
            new(3, null, null)
        ];

        IReadOnlyList<H264ReferencePicture> result =
            _builder.BuildList0(references, 7, 16, 3, modifications);

        Assert.Equal(new[] { 5, 6, 4 }, result.Select(picture => picture.FrameNum));
    }

    [Fact]
    public void BuildList0_ShouldApplyAddShortTermModificationWithPicNumWrap()
    {
        H264ReferencePicture[] references =
        [
            new(15, 150),
            new(2, 20),
            new(13, 130)
        ];
        H264ReferenceListModification[] modifications =
        [
            new(1, 0, null), // CurrPicNum 14 -> PicNumNoWrap 15, wrapped PicNum -1
            new(3, null, null)
        ];

        IReadOnlyList<H264ReferencePicture> result =
            _builder.BuildList0(references, 14, 16, 3, modifications);

        Assert.Equal(new[] { 15, 13, 2 }, result.Select(picture => picture.FrameNum));
    }

    [Fact]
    public void BuildList0_ShouldInsertLongTermPictureAtRequestedIndex()
    {
        H264ReferencePicture[] references =
        [
            new(4, 40),
            new(7, 70, true, 2),
            new(5, 50, true, 0)
        ];
        H264ReferenceListModification[] modifications =
        [
            new(2, null, 2),
            new(3, null, null)
        ];

        IReadOnlyList<H264ReferencePicture> result =
            _builder.BuildList0(references, 6, 16, 3, modifications);

        Assert.Equal(new[] { 7, 4, 5 }, result.Select(picture => picture.FrameNum));
    }

    [Fact]
    public void BuildList0_ShouldRemoveDuplicateWhenMovingSelectedPictureForward()
    {
        H264ReferencePicture[] references =
        [
            new(5, 50),
            new(4, 40),
            new(3, 30)
        ];
        H264ReferenceListModification[] modifications =
        [
            new(0, 1, null), // select PicNum 4, already at index 1
            new(3, null, null)
        ];

        IReadOnlyList<H264ReferencePicture> result =
            _builder.BuildList0(references, 6, 16, 3, modifications);

        Assert.Equal(new[] { 4, 5, 3 }, result.Select(picture => picture.FrameNum));
    }

    [Fact]
    public void BuildList0_ShouldRejectUnavailableReferenceAndMalformedModification()
    {
        H264ReferencePicture[] references = [new(4, 40)];

        Assert.Throws<FormatException>(() => _builder.BuildList0(
            references, 6, 16, 1,
            [new H264ReferenceListModification(0, 2, null), new(3, null, null)]));

        Assert.Throws<FormatException>(() => _builder.BuildList0(
            references, 6, 16, 1,
            [new H264ReferenceListModification(3, null, null), new(3, null, null)]));

        Assert.Throws<FormatException>(() => _builder.BuildList0(
            references, 6, 16, 1,
            [new H264ReferenceListModification(0, 0, null)]));
    }

    [Fact]
    public void BuildList0_ShouldRejectDuplicatePictureNumbersAndInsufficientReferences()
    {
        Assert.Throws<FormatException>(() => _builder.BuildList0(
            [new H264ReferencePicture(2, 20), new H264ReferencePicture(2, 21)],
            3, 16, 1));

        Assert.Throws<FormatException>(() => _builder.BuildList0(
            [new H264ReferencePicture(2, 20)],
            3, 16, 2));
    }

    [Fact]
    public void BuildList0_ShouldValidateCurrentFrameAndCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _builder.BuildList0([], -1, 16, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _builder.BuildList0([], 16, 16, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _builder.BuildList0([], 0, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _builder.BuildList0([], 0, 16, 0));
    }
}

public sealed class H264MotionVectorRangeTests
{
    [Theory]
    [InlineData(-40000, -32768)]
    [InlineData(-32768, -32768)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(32767, 32767)]
    [InlineData(40000, 32767)]
    public void ClipComponent_ShouldClipToNormativeSignedSixteenBitRange(int input, int expected)
    {
        Assert.Equal(expected, H264MotionVectorRange.ClipComponent(input));
    }

    [Fact]
    public void Clip_ShouldClipBothVectorComponentsIndependently()
    {
        Assert.Equal(
            new H264MotionVector(32767, -32768),
            H264MotionVectorRange.Clip(new H264MotionVector(40000, -40000)));
    }
}

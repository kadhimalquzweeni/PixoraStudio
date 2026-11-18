using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264PictureOrderCountCalculatorTests
{
    [Fact]
    public void Calculate_TypeZero_ShouldHandleLsbWraparound()
    {
        H264PictureOrderCountCalculator calculator = new();
        H264SequenceParameterSet sps = CreateSps();
        H264PictureOrderCount first = calculator.Calculate(sps, CreateSlice(frameNum: 0, pocLsb: 14, idr: true), 3);
        H264PictureOrderCount second = calculator.Calculate(sps, CreateSlice(frameNum: 1, pocLsb: 1), 3);

        Assert.Equal(14, first.PictureOrderCount);
        Assert.Equal(17, second.PictureOrderCount);
        Assert.Equal(17, second.TopFieldOrderCount);
    }

    [Fact]
    public void Calculate_TypeZero_ShouldUseDeltaBottomAndNonReferencePictures()
    {
        H264PictureOrderCountCalculator calculator = new();
        H264SequenceParameterSet sps = CreateSps();
        calculator.Calculate(sps, CreateSlice(frameNum: 0, pocLsb: 4, idr: true), 3);
        H264PictureOrderCount result = calculator.Calculate(
            sps, CreateSlice(frameNum: 1, pocLsb: 6, deltaBottom: -2), 0);

        Assert.Equal(6, result.TopFieldOrderCount);
        Assert.Equal(4, result.BottomFieldOrderCount);
        Assert.Equal(4, result.PictureOrderCount);
    }

    [Fact]
    public void Calculate_TypeZero_ShouldCalculateBottomFieldOnly()
    {
        H264PictureOrderCount result = new H264PictureOrderCountCalculator().Calculate(
            CreateSps(), CreateSlice(frameNum: 0, pocLsb: 9, idr: true, field: true, bottom: true), 3);

        Assert.False(result.HasTopField);
        Assert.True(result.HasBottomField);
        Assert.Equal(9, result.PictureOrderCount);
        Assert.Equal(int.MaxValue, result.TopFieldOrderCount);
    }

    [Fact]
    public void Calculate_TypeOne_ShouldApplyReferenceCycleAndNonReferenceOffset()
    {
        H264SequenceParameterSet sps = CreateSps() with
        {
            PicOrderCntType = 1,
            OffsetForNonRefPic = -1,
            OffsetForTopToBottomField = 2,
            OffsetForRefFrame = [2, 3]
        };
        H264PictureOrderCountCalculator calculator = new();
        calculator.Calculate(sps, CreateSlice(0, null, idr: true), 3);
        H264PictureOrderCount reference = calculator.Calculate(sps, CreateSlice(1, null), 3);
        H264PictureOrderCount nonReference = calculator.Calculate(sps, CreateSlice(2, null), 0);

        Assert.Equal(2, reference.TopFieldOrderCount);
        Assert.Equal(4, reference.BottomFieldOrderCount);
        Assert.Equal(1, nonReference.TopFieldOrderCount);
        Assert.Equal(3, nonReference.BottomFieldOrderCount);
    }

    [Fact]
    public void Calculate_TypeOne_ShouldCalculateBottomFieldPoc()
    {
        H264SequenceParameterSet sps = CreateSps() with
        {
            PicOrderCntType = 1,
            OffsetForTopToBottomField = 5,
            OffsetForRefFrame = [2]
        };
        H264PictureOrderCount result = new H264PictureOrderCountCalculator().Calculate(
            sps, CreateSlice(1, null, field: true, bottom: true), 3);

        Assert.False(result.HasTopField);
        Assert.Equal(7, result.BottomFieldOrderCount);
    }

    [Fact]
    public void Calculate_TypeTwo_ShouldDeriveReferenceAndNonReferenceOrder()
    {
        H264PictureOrderCountCalculator calculator = new();
        H264SequenceParameterSet sps = CreateSps() with { PicOrderCntType = 2 };
        calculator.Calculate(sps, CreateSlice(0, null, idr: true), 3);

        H264PictureOrderCount reference = calculator.Calculate(sps, CreateSlice(1, null), 3);
        H264PictureOrderCount nonReference = calculator.Calculate(sps, CreateSlice(2, null), 0);

        Assert.Equal(2, reference.PictureOrderCount);
        Assert.Equal(3, nonReference.PictureOrderCount);
    }

    [Fact]
    public void Calculate_ShouldResetFrameNumberStateAfterMmcoFive()
    {
        H264PictureOrderCountCalculator calculator = new();
        H264SequenceParameterSet sps = CreateSps() with { PicOrderCntType = 2 };
        calculator.Calculate(sps, CreateSlice(0, null, idr: true), 3);
        calculator.Calculate(sps, CreateSlice(7, null, marking: CreateMmco5()), 3);

        H264PictureOrderCount result = calculator.Calculate(sps, CreateSlice(1, null), 3);

        Assert.Equal(2, result.PictureOrderCount);
        Assert.Equal(0, result.FrameNumOffset);
    }

    [Fact]
    public void Calculate_ShouldRejectMissingRequiredPocSyntax()
    {
        H264SequenceParameterSet sps = CreateSps();
        H264SliceHeader slice = CreateSlice(0, null, idr: true);
        Assert.Throws<FormatException>(() => new H264PictureOrderCountCalculator().Calculate(sps, slice, 3));
    }

    private static H264SequenceParameterSet CreateSps() => new(
        0, 66, 30, 1, false, 0, 0, 0, 0, 0, 2,
        39, 22, true, true, 640, 368, false);

    private static H264SliceHeader CreateSlice(
        uint frameNum,
        uint? pocLsb,
        int? deltaBottom = null,
        bool idr = false,
        bool field = false,
        bool bottom = false,
        H264DecodedReferenceMarking? marking = null) =>
        new(0, H264SliceType.I, 0, 0, frameNum, field, bottom,
            idr ? 0u : null, pocLsb, deltaBottom, null, null, null,
            false, false, 0, 0, [], [], null, marking, null, 0, false, null,
            null, null, null, null);

    private static H264DecodedReferenceMarking CreateMmco5() =>
        new(false, false, true,
            [new H264MemoryManagementControlOperation(5, null, null, null, null)]);
}

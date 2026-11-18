using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264DeblockingBoundaryStrengthDeriverTests
{
    private static readonly H264ReferencePicture Reference0 = new(0, 10);
    private static readonly H264ReferencePicture Reference1 = new(1, 20);

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 3)]
    public void Derive_WhenEitherBlockIsIntra_ShouldUseIntraBoundaryStrength(
        bool macroblockBoundary, int expected)
    {
        H264DeblockingBlockMetadata p = Intra();
        H264DeblockingBlockMetadata q = Inter();

        Assert.Equal(expected,
            H264DeblockingBoundaryStrengthDeriver.Derive(p, q, macroblockBoundary));
    }

    [Fact]
    public void Derive_WhenEitherBlockHasNonZeroCoefficients_ShouldReturnTwo()
    {
        H264DeblockingBlockMetadata p = Inter() with { HasNonZeroCoefficients = true };

        Assert.Equal(2,
            H264DeblockingBoundaryStrengthDeriver.Derive(p, Inter(), false));
    }

    [Fact]
    public void Derive_WhenReferencePicturesAndMotionVectorsMatch_ShouldReturnZero()
    {
        H264DeblockingBlockMetadata p = Inter() with
        {
            ReferencePictureL0 = Reference0,
            MotionVectorL0 = new H264MotionVector(12, -8)
        };
        H264DeblockingBlockMetadata q = p with
        {
            MotionVectorL0 = new H264MotionVector(15, -5)
        };

        Assert.Equal(0,
            H264DeblockingBoundaryStrengthDeriver.Derive(p, q, false));
    }

    [Fact]
    public void Derive_WhenReferencePictureDiffers_ShouldReturnOne()
    {
        H264DeblockingBlockMetadata p = Inter() with
        {
            ReferencePictureL0 = Reference0,
            MotionVectorL0 = new H264MotionVector(0, 0)
        };
        H264DeblockingBlockMetadata q = Inter() with
        {
            ReferencePictureL0 = Reference1,
            MotionVectorL0 = new H264MotionVector(0, 0)
        };

        Assert.Equal(1,
            H264DeblockingBoundaryStrengthDeriver.Derive(p, q, false));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(-4, 1)]
    public void Derive_WhenMotionVectorDifferenceCrossesQuarterSampleThreshold_ShouldReturnExpected(
        int horizontalDifference, int expected)
    {
        H264DeblockingBlockMetadata p = Inter() with
        {
            ReferencePictureL0 = Reference0,
            MotionVectorL0 = new H264MotionVector(0, 0)
        };
        H264DeblockingBlockMetadata q = p with
        {
            MotionVectorL0 = new H264MotionVector(horizontalDifference, 0)
        };

        Assert.Equal(expected,
            H264DeblockingBoundaryStrengthDeriver.Derive(p, q, false));
    }

    [Fact]
    public void Derive_WhenBListPredictionsAreSwappedButEquivalent_ShouldReturnZero()
    {
        H264DeblockingBlockMetadata p = Inter() with
        {
            ReferencePictureL0 = Reference0,
            MotionVectorL0 = new H264MotionVector(4, 8),
            ReferencePictureL1 = Reference1,
            MotionVectorL1 = new H264MotionVector(-4, 0)
        };
        H264DeblockingBlockMetadata q = Inter() with
        {
            ReferencePictureL0 = Reference1,
            MotionVectorL0 = new H264MotionVector(-3, 1),
            ReferencePictureL1 = Reference0,
            MotionVectorL1 = new H264MotionVector(5, 7)
        };

        Assert.Equal(0,
            H264DeblockingBoundaryStrengthDeriver.Derive(p, q, false));
    }

    [Fact]
    public void Derive_WhenPredictionListsDiffer_ShouldReturnOne()
    {
        H264DeblockingBlockMetadata p = Inter() with
        {
            ReferencePictureL0 = Reference0,
            MotionVectorL0 = new H264MotionVector(0, 0)
        };
        H264DeblockingBlockMetadata q = Inter() with
        {
            ReferencePictureL0 = Reference0,
            MotionVectorL0 = new H264MotionVector(4, 0)
        };

        Assert.Equal(1,
            H264DeblockingBoundaryStrengthDeriver.Derive(p, q, false));
    }

    private static H264DeblockingBlockMetadata Intra() =>
        new(true, false, null, null, null, null);

    private static H264DeblockingBlockMetadata Inter() =>
        new(false, false, null, null, null, null);
}

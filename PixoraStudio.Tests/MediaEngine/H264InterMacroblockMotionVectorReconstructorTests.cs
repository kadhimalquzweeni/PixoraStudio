using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterMacroblockMotionVectorReconstructorTests
{
    private readonly H264InterMacroblockMotionVectorReconstructor _reconstructor = new();

    [Fact]
    public void Reconstruct_P16x16_ShouldAddMvdAndRecordWholeMacroblock()
    {
        H264MotionVectorPictureState state = new(1, 1);
        H264InterMacroblockSyntax syntax = CreateRectangularSyntax(
            0, (0, 0, 16, 16, new H264MotionVectorDifference(3, -2)));

        IReadOnlyList<H264ReconstructedInterPartition> result =
            _reconstructor.Reconstruct(syntax, state, 0, 0, 4, 1);

        H264ReconstructedInterPartition partition = Assert.Single(result);
        Assert.Equal(new H264MotionVector(3, -2), partition.MotionVector);
        Assert.Equal((0, 0, 4, 4), (partition.X, partition.Y, partition.Width, partition.Height));

        H264MotionVectorNeighbourCandidates candidates =
            state.GetCandidates(0, 0, 4, 4, 4);
        Assert.Null(candidates.Left);
        Assert.Null(candidates.Above);
        Assert.Null(candidates.AboveRight);
    }

    [Fact]
    public void Reconstruct_P16x8_ShouldUseLeftCandidateForBottomPartition()
    {
        H264MotionVectorPictureState state = new(2, 1);
        state.RecordInterPartition(0, 0, 4, 4, 8, 0, new H264MotionVector(9, 6));
        H264InterMacroblockSyntax syntax = CreateRectangularSyntax(
            1,
            (0, 0, 16, 8, new H264MotionVectorDifference(0, 0)),
            (0, 8, 16, 8, new H264MotionVectorDifference(1, 2)));

        IReadOnlyList<H264ReconstructedInterPartition> result =
            _reconstructor.Reconstruct(syntax, state, 1, 0, 8, 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(new H264MotionVector(9, 6), result[0].MotionVector);
        Assert.Equal(new H264MotionVector(10, 8), result[1].MotionVector);
    }

    [Fact]
    public void Reconstruct_P8x16_ShouldUseLeftCandidateForLeftPartitionAndAboveRightForRight()
    {
        H264MotionVectorPictureState state = new(2, 2);
        state.RecordInterPartition(0, 4, 4, 4, 2, 0, new H264MotionVector(4, 5));
        state.RecordInterPartition(8, 0, 4, 4, 2, 0, new H264MotionVector(7, 3));
        H264InterMacroblockSyntax syntax = CreateRectangularSyntax(
            2,
            (0, 0, 8, 16, new H264MotionVectorDifference(0, 0)),
            (8, 0, 8, 16, new H264MotionVectorDifference(0, 0)));

        IReadOnlyList<H264ReconstructedInterPartition> result =
            _reconstructor.Reconstruct(syntax, state, 1, 1, 2, 1);

        Assert.Equal(2, result.Count);
        Assert.Equal(new H264MotionVector(4, 5), result[0].MotionVector);
        // The right partition's C candidate is at x=8,y=3 (above-right).
        Assert.Equal(new H264MotionVector(7, 3), result[1].MotionVector);
    }

    [Fact]
    public void Reconstruct_P8x8_ShouldReconstructAllSixteenFourByFourSubPartitionsInOrder()
    {
        H264MotionVectorPictureState state = new(1, 1);
        H264InterPredictionPartition[] partitions = new H264InterPredictionPartition[4];
        for (int i = 0; i < 4; i++)
        {
            int originX = (i & 1) * 8;
            int originY = (i >> 1) * 8;
            H264InterSubPartition[] subPartitions = new H264InterSubPartition[4];
            for (int j = 0; j < 4; j++)
            {
                subPartitions[j] = new H264InterSubPartition(
                    originX + (j & 1) * 4,
                    originY + (j >> 1) * 4,
                    4, 4, [new H264MotionVectorDifference(i + j, j - i)]);
            }
            partitions[i] = new H264InterPredictionPartition(
                originX, originY, 8, 8, 0, subPartitions);
        }

        H264InterMacroblockSyntax syntax = new(3, true, false, partitions);

        IReadOnlyList<H264ReconstructedInterPartition> result =
            _reconstructor.Reconstruct(syntax, state, 0, 0, 1, 1);

        Assert.Equal(16, result.Count);
        Assert.Equal(
            new[] { (0, 0), (1, 0), (0, 1), (1, 1) },
            result.Take(4).Select(partition => (partition.X, partition.Y)));
        Assert.Equal(new H264MotionVector(0, 0), result[0].MotionVector);
        Assert.Equal(new H264MotionVector(1, 1), result[1].MotionVector);
    }

    [Fact]
    public void Reconstruct_P8x8ref0_ShouldUseReferenceIndexZeroWithoutChangingParsedMvd()
    {
        H264MotionVectorPictureState state = new(1, 1);
        H264InterPredictionPartition[] partitions =
        [
            new(0, 0, 8, 8, 0,
                [new H264InterSubPartition(0, 0, 8, 8, [new(2, 3)])]),
            new(8, 0, 8, 8, 0,
                [new H264InterSubPartition(8, 0, 8, 8, [new(-1, 4)])]),
            new(0, 8, 8, 8, 0,
                [new H264InterSubPartition(0, 8, 8, 8, [new(0, 0)])]),
            new(8, 8, 8, 8, 0,
                [new H264InterSubPartition(8, 8, 8, 8, [new(1, -2)])])
        ];

        IReadOnlyList<H264ReconstructedInterPartition> result =
            _reconstructor.Reconstruct(
                new H264InterMacroblockSyntax(4, true, true, partitions),
                state, 0, 0, 0, 1);

        Assert.All(result, partition => Assert.Equal(0, partition.ReferenceIndex));
        Assert.Equal(new H264MotionVector(2, 3), result[0].MotionVector);
        Assert.Equal(new H264MotionVector(-1, 4), result[1].MotionVector);
    }

    [Fact]
    public void ReconstructSkipped_ShouldForceZeroWhenLeftOrAboveIsUnavailable()
    {
        H264MotionVectorPictureState state = new(2, 2);
        state.RecordInterPartition(4, 0, 4, 4, 5, 0, new H264MotionVector(8, 6));

        H264ReconstructedInterPartition result =
            _reconstructor.ReconstructSkipped(state, 1, 1, 5);

        Assert.Equal(new H264MotionVector(0, 0), result.MotionVector);
        Assert.Equal(0, result.ReferenceIndex);
        Assert.Equal((4, 4, 4, 4), (result.X, result.Y, result.Width, result.Height));
    }

    [Fact]
    public void ReconstructSkipped_ShouldUseNormalPredictorWhenLeftAndAboveAreNonZero()
    {
        H264MotionVectorPictureState state = new(3, 2);
        state.RecordInterPartition(0, 4, 4, 4, 6, 0, new H264MotionVector(8, 4));
        state.RecordInterPartition(4, 0, 4, 4, 6, 0, new H264MotionVector(8, 4));
        state.RecordInterPartition(8, 0, 4, 4, 6, 0, new H264MotionVector(8, 4));

        H264ReconstructedInterPartition result =
            _reconstructor.ReconstructSkipped(state, 1, 1, 6);

        Assert.Equal(new H264MotionVector(8, 4), result.MotionVector);
    }

    [Fact]
    public void Reconstruct_ShouldRejectReferenceOutsideActiveListAndMalformedGeometry()
    {
        H264MotionVectorPictureState state = new(1, 1);

        H264InterMacroblockSyntax invalidReference = new(
            0, false, false,
            [new H264InterPredictionPartition(0, 0, 16, 16, 1,
                [new H264InterSubPartition(0, 0, 16, 16, [new(0, 0)])])]);
        Assert.Throws<FormatException>(() =>
            _reconstructor.Reconstruct(invalidReference, state, 0, 0, 0, 1));

        H264InterMacroblockSyntax invalidGeometry = new(
            0, false, false,
            [new H264InterPredictionPartition(0, 0, 16, 16, 0,
                [new H264InterSubPartition(0, 0, 12, 16, [new(0, 0)])])]);
        Assert.Throws<FormatException>(() =>
            _reconstructor.Reconstruct(invalidGeometry, new H264MotionVectorPictureState(1, 1), 0, 0, 0, 1));

        H264InterMacroblockSyntax invalidCount = new(0, false, false, []);
        Assert.Throws<FormatException>(() =>
            _reconstructor.Reconstruct(invalidCount, new H264MotionVectorPictureState(1, 1), 0, 0, 0, 1));
    }

    private static H264InterMacroblockSyntax CreateRectangularSyntax(
        uint macroblockType,
        params (int X, int Y, int Width, int Height, H264MotionVectorDifference Mvd)[] entries) =>
        CreateRectangularSyntax(macroblockType, entries, 0);

    private static H264InterMacroblockSyntax CreateRectangularSyntax(
        uint macroblockType,
        (int X, int Y, int Width, int Height, H264MotionVectorDifference Mvd)[] entries,
        uint referenceIndex)
    {
        H264InterPredictionPartition[] partitions = entries.Select(entry =>
            new H264InterPredictionPartition(
                entry.X, entry.Y, entry.Width, entry.Height, referenceIndex,
                [new H264InterSubPartition(
                    entry.X, entry.Y, entry.Width, entry.Height, [entry.Mvd])])).ToArray();
        return new H264InterMacroblockSyntax(macroblockType, false, false, partitions);
    }
}

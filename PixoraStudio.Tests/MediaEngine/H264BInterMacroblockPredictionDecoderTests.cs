using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BInterMacroblockPredictionDecoderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DecodeAndPredict_List0Only_ShouldUseSelectedReference(uint chromaFormat)
    {
        H264DecodedPictureSurface reference = CreateSurface(chromaFormat, 47);
        H264BReferencePictureLists metadata = new(
            [new H264ReferencePicture(0, -2)], [new H264ReferencePicture(1, 4)]);
        H264ReconstructedBInterSubPartition partition = new(
            0, 0, 0, 0, 4, 4, 0, null, new H264MotionVector(0, 0), null);

        H264BInterMacroblockPrediction result = Assert.Single(
            new H264BInterMacroblockPredictionDecoder().DecodeAndPredict(
                [partition], [reference], [reference], metadata,
                chromaFormat, 0, 0));

        Assert.All(result.Prediction.Luma, value => Assert.Equal((byte)47, value));
    }

    [Fact]
    public void DecodeAndPredict_List1Only_ShouldUseSelectedBackwardReference()
    {
        H264DecodedPictureSurface reference0 = CreateSurface(1, 31);
        H264DecodedPictureSurface reference1 = CreateSurface(1, 59);
        H264BReferencePictureLists metadata = new(
            [new H264ReferencePicture(0, -2)], [new H264ReferencePicture(1, 4)]);
        H264ReconstructedBInterSubPartition partition = new(
            0, 0, 0, 0, 4, 4, null, 0, null, new H264MotionVector(0, 0));

        H264BInterMacroblockPrediction result = Assert.Single(
            new H264BInterMacroblockPredictionDecoder().DecodeAndPredict(
                [partition], [reference0], [reference1], metadata,
                1, 0, 0));

        Assert.All(result.Prediction.Luma, value => Assert.Equal((byte)59, value));
    }

    [Fact]
    public void DecodeAndPredict_BiPrediction_ShouldCombineBothListsAndAssembleMacroblock()
    {
        H264DecodedPictureSurface reference0 = CreateSurface(1, 40);
        H264DecodedPictureSurface reference1 = CreateSurface(1, 60);
        H264BReferencePictureLists metadata = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 10)]);
        H264ReconstructedBInterSubPartition partition = new(
            0, 0, 0, 0, 4, 4, 0, 0,
            new H264MotionVector(0, 0), new H264MotionVector(0, 0));

        H264BInterMacroblockPrediction result = Assert.Single(
            new H264BInterMacroblockPredictionDecoder().DecodeAndPredict(
                [partition], [reference0], [reference1], metadata,
                1, 5, 0));

        Assert.All(result.Prediction.Luma, value => Assert.Equal((byte)50, value));
    }

    [Fact]
    public void DecodeAndPredict_ExplicitWeightedBiPrediction_ShouldUseParsedWeightTable()
    {
        H264DecodedPictureSurface reference0 = CreateSurface(1, 100);
        H264DecodedPictureSurface reference1 = CreateSurface(1, 50);
        H264BReferencePictureLists metadata = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 10)]);
        H264ReconstructedBInterSubPartition partition = new(
            0, 0, 0, 0, 4, 4, 0, 0,
            new H264MotionVector(0, 0), new H264MotionVector(0, 0));
        H264PredictionWeight weight0 = new(true, 2, 3, true, [4, 2], [1, -1]);
        H264PredictionWeight weight1 = new(true, 2, 5, true, [4, 6], [3, 5]);
        H264PredictionWeightTable table = new(1, 2, [weight0], [weight1]);

        H264BInterMacroblockPrediction result = Assert.Single(
            new H264BInterMacroblockPredictionDecoder().DecodeAndPredict(
                [partition], [reference0], [reference1], metadata,
                1, 5, 1, table));

        Assert.All(result.Prediction.Luma, value => Assert.Equal((byte)79, value));
    }

    [Fact]
    public void DecodeAndPredict_ImplicitWeightedBiPrediction_ShouldUseReferencePocs()
    {
        H264DecodedPictureSurface reference0 = CreateSurface(1, 10);
        H264DecodedPictureSurface reference1 = CreateSurface(1, 30);
        H264BReferencePictureLists metadata = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 10)]);
        H264ReconstructedBInterSubPartition partition = new(
            0, 0, 0, 0, 4, 4, 0, 0,
            new H264MotionVector(0, 0), new H264MotionVector(0, 0));

        H264BInterMacroblockPrediction result = Assert.Single(
            new H264BInterMacroblockPredictionDecoder().DecodeAndPredict(
                [partition], [reference0], [reference1], metadata,
                1, 5, 2));

        Assert.All(result.Prediction.Luma, value => Assert.Equal((byte)20, value));
    }

    [Fact]
    public void Assemble_ShouldRequireCompleteNonOverlappingMacroblockCoverage()
    {
        H264DecodedPictureSurface reference = CreateSurface(1, 33);
        H264BReferencePictureLists metadata = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 1)]);
        H264ReconstructedBInterSubPartition topLeft = new(
            0, 0, 0, 0, 2, 2, 0, null, new H264MotionVector(0, 0), null);
        H264BInterMacroblockPrediction prediction = Assert.Single(
            new H264BInterMacroblockPredictionDecoder().DecodeAndPredict(
                [topLeft], [reference], [reference], metadata, 1, 0, 0));

        Assert.Throws<FormatException>(() =>
            H264BInterMacroblockPredictionDecoder.Assemble([prediction], 1));
    }

    [Fact]
    public void DecodeAndPredict_ShouldRejectMissingReferencesAndExplicitWeights()
    {
        H264BReferencePictureLists metadata = new(
            [new H264ReferencePicture(0, 0)], [new H264ReferencePicture(1, 1)]);
        H264ReconstructedBInterSubPartition partition = new(
            0, 0, 0, 0, 4, 4, 0, 0,
            new H264MotionVector(0, 0), new H264MotionVector(0, 0));
        var decoder = new H264BInterMacroblockPredictionDecoder();
        H264DecodedPictureSurface surface = CreateSurface(1, 20);

        Assert.Throws<ArgumentNullException>(() => decoder.DecodeAndPredict(
            [partition], [surface], [surface], metadata, 1, 0, 1));
        Assert.Throws<ArgumentException>(() => decoder.DecodeAndPredict(
            [partition], [], [surface], metadata, 1, 0, 0));
    }

    private static H264DecodedPictureSurface CreateSurface(uint format, byte value)
    {
        H264SequenceParameterSet sps = new(
            0, 66, 30, format, false, 0, 0, 0, 0, 0, 1,
            1, 1, true, true, 32, 32, false);
        H264DecodedPictureSurface surface = new(sps);
        int chromaHeight = format == 1 ? 8 : 16;
        surface.WriteMacroblock(
            0, 0, 0, false,
            Enumerable.Repeat(value, 256).ToArray(),
            Enumerable.Repeat(value, 8 * chromaHeight).ToArray(),
            Enumerable.Repeat(value, 8 * chromaHeight).ToArray());
        return surface;
    }
}

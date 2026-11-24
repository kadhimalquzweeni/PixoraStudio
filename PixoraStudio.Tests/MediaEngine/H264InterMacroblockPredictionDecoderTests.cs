using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264InterMacroblockPredictionDecoderTests
{
    private readonly H264InterMacroblockPredictionDecoder _decoder = new();

    [Fact]
    public void DecodeAndPredict_ShouldSelectReferenceByParsedIndexAndGenerateLumaChroma()
    {
        H264DecodedPictureSurface first = CreateReference(20, 30, 40);
        H264DecodedPictureSurface second = CreateReference(100, 110, 120);
        BitWriter bits = new();
        bits.WriteUe(1); // ref_idx_l0 = 1
        bits.WriteSe(0);
        bits.WriteSe(0);

        IReadOnlyList<H264InterMacroblockPrediction> result =
            _decoder.DecodeAndPredict(
                new H264BitReader(bits.ToArray()),
                CreateInterHeader(0),
                1,
                [first, second],
                new H264MotionVectorPictureState(1, 1),
                0, 0, 5);

        H264InterMacroblockPrediction prediction = Assert.Single(result);
        Assert.Equal(1, prediction.Partition.ReferenceIndex);
        Assert.Equal(256, prediction.Prediction.Luma.Length);
        Assert.All(prediction.Prediction.Luma, sample => Assert.Equal((byte)100, sample));
        Assert.All(prediction.Prediction.Cb, sample => Assert.Equal((byte)110, sample));
        Assert.All(prediction.Prediction.Cr, sample => Assert.Equal((byte)120, sample));
    }

    [Fact]
    public void DecodeSkippedAndPredict_ShouldUseFirstReferenceAndPskipVector()
    {
        H264DecodedPictureSurface reference = CreateReference(77, 88, 99);
        H264MacroblockHeader skipped = new(0, 0, true, 1,
            false, false, null, null, null, false);

        H264InterMacroblockPrediction result =
            _decoder.DecodeSkippedAndPredict(
                skipped, [reference], new H264MotionVectorPictureState(1, 1),
                0, 0, 3);

        Assert.Equal(new H264MotionVector(0, 0), result.Partition.MotionVector);
        Assert.All(result.Prediction.Luma, sample => Assert.Equal((byte)77, sample));
        Assert.All(result.Prediction.Cb, sample => Assert.Equal((byte)88, sample));
        Assert.All(result.Prediction.Cr, sample => Assert.Equal((byte)99, sample));
    }

    [Fact]
    public void DecodeAndPredict_ShouldRequireExactlyTheActiveReferenceList()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteSe(0);
        bits.WriteSe(0);

        Assert.Throws<ArgumentException>(() => _decoder.DecodeAndPredict(
            new H264BitReader(bits.ToArray()), CreateInterHeader(0), 1,
            [CreateReference(1, 2, 3)],
            new H264MotionVectorPictureState(1, 1), 0, 0, 0));
    }

    [Fact]
    public void DecodeSkippedAndPredict_ShouldRejectEmptyReferenceList()
    {
        H264MacroblockHeader skipped = new(0, 0, true, 1,
            false, false, null, null, null, false);

        Assert.Throws<ArgumentException>(() => _decoder.DecodeSkippedAndPredict(
            skipped, [], new H264MotionVectorPictureState(1, 1), 0, 0, 0));
    }

    [Fact]
    public void DecodeAndPredict_ShouldApplySliceWeightTableToSelectedReference()
    {
        H264DecodedPictureSurface reference = CreateReference(100, 110, 120);
        BitWriter bits = new();
        bits.WriteUe(0); // ref_idx_l0 = 0
        bits.WriteSe(0);
        bits.WriteSe(0);
        H264PredictionWeight weight = new(true, 2, 0, false, [], []);
        H264PredictionWeightTable table = new(0, 0, [weight], []);

        IReadOnlyList<H264InterMacroblockPrediction> result =
            _decoder.DecodeAndPredict(
                new H264BitReader(bits.ToArray()),
                CreateInterHeader(0),
                0,
                [reference],
                new H264MotionVectorPictureState(1, 1),
                0, 0, 5,
                table);

        H264InterMacroblockPrediction prediction = Assert.Single(result);
        Assert.All(prediction.Prediction.Luma, sample => Assert.Equal((byte)200, sample));
        Assert.All(prediction.Prediction.Cb, sample => Assert.Equal((byte)110, sample));
        Assert.All(prediction.Prediction.Cr, sample => Assert.Equal((byte)120, sample));
    }


    [Fact]
    public void PredictFromSyntax_ShouldReconstructCabacSyntaxAndGeneratePrediction()
    {
        H264DecodedPictureSurface reference = CreateReference(91, 82, 73);
        H264InterMacroblockSyntax syntax = new(
            0,
            false,
            false,
            [
                new H264InterPredictionPartition(
                    0, 0, 16, 16, 0,
                    [new H264InterSubPartition(0, 0, 16, 16,
                        [new H264MotionVectorDifference(0, 0)])])
            ]);
        H264MotionVectorPictureState motionState = new(1, 1);

        IReadOnlyList<H264InterMacroblockPrediction> result =
            _decoder.PredictFromSyntax(
                syntax, 0, [reference], motionState, 0, 0, 4);

        H264InterMacroblockPrediction prediction = Assert.Single(result);
        Assert.Equal(0, prediction.Partition.ReferenceIndex);
        Assert.Equal(new H264MotionVector(0, 0), prediction.Partition.MotionVector);
        Assert.All(prediction.Prediction.Luma, sample => Assert.Equal((byte)91, sample));
        Assert.All(prediction.Prediction.Cb, sample => Assert.Equal((byte)82, sample));
        Assert.All(prediction.Prediction.Cr, sample => Assert.Equal((byte)73, sample));
        Assert.True(motionState.TryGetStoredMotionVector(0, 0, out H264MotionVectorCandidate stored));
        Assert.Equal(0, stored.ReferenceIndex);
    }

    [Fact]
    public void PredictFromSyntax_ShouldApplyCABACReferenceIndexAndMotionVectorDifference()
    {
        H264DecodedPictureSurface reference = CreateReference(50, 60, 70);
        H264InterMacroblockSyntax syntax = new(
            0,
            false,
            false,
            [
                new H264InterPredictionPartition(
                    0, 0, 16, 16, 0,
                    [new H264InterSubPartition(0, 0, 16, 16,
                        [new H264MotionVectorDifference(2, -2)])])
            ]);

        IReadOnlyList<H264InterMacroblockPrediction> result =
            _decoder.PredictFromSyntax(
                syntax, 0, [reference],
                new H264MotionVectorPictureState(1, 1), 0, 0, 0);

        H264InterMacroblockPrediction prediction = Assert.Single(result);
        Assert.Equal(new H264MotionVector(2, -2), prediction.Partition.MotionVector);
        Assert.Equal(256, prediction.Prediction.Luma.Length);
    }

    [Fact]
    public void PredictFromSyntax_ShouldRejectInvalidMotionStateBeforeReconstruction()
    {
        H264DecodedPictureSurface reference = CreateReference(1, 2, 3);
        H264InterMacroblockSyntax syntax = new(
            0, false, false,
            [
                new H264InterPredictionPartition(0, 0, 16, 16, 0,
                    [new H264InterSubPartition(0, 0, 16, 16,
                        [new H264MotionVectorDifference(0, 0)])])
            ]);

        Assert.Throws<ArgumentException>(() => _decoder.PredictFromSyntax(
            syntax, 0, [reference], new H264MotionVectorPictureState(2, 1),
            0, 0, 0));
    }

    private static H264MacroblockHeader CreateInterHeader(uint type) =>
        new(0, type, false, 0, false, false, null, null, null, false);

    private static H264DecodedPictureSurface CreateReference(
        byte lumaValue, byte cbValue, byte crValue)
    {
        H264SequenceParameterSet sps = new(
            0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 16, 16, false);
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(
            0, 0, 0, false,
            Enumerable.Repeat(lumaValue, 256).ToArray(),
            Enumerable.Repeat(cbValue, 64).ToArray(),
            Enumerable.Repeat(crValue, 64).ToArray());
        return surface;
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteUe(uint value)
        {
            uint codeNum = checked(value + 1);
            int leadingZeroes = 0;
            for (uint current = codeNum; current > 1; current >>= 1)
                leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++)
                _bits.Add(false);
            for (int i = leadingZeroes; i >= 0; i--)
                _bits.Add(((codeNum >> i) & 1) != 0);
        }

        public void WriteSe(int value)
        {
            uint codeNum = value <= 0
                ? checked((uint)(-2L * value))
                : checked((uint)(2L * value - 1));
            WriteUe(codeNum);
        }

        public byte[] ToArray()
        {
            byte[] bytes = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i])
                    bytes[i / 8] |= (byte)(1 << (7 - i % 8));
            return bytes;
        }
    }
}

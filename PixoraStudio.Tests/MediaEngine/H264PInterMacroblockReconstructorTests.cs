using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264PInterMacroblockReconstructorTests
{
    private readonly H264PInterMacroblockReconstructor _decoder = new();

    [Fact]
    public void DecodeAndReconstruct_WithNoResidual_ShouldCommitReferencePrediction()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Surface(sps, 100, 110, 120);
        H264DecodedPictureSurface destination = new(sps);

        H264InterMacroblockReconstructionResult result = _decoder.DecodeAndReconstruct(
            Bits("111"), Header(), 0, [reference],
            new H264MotionVectorPictureState(1, 1), destination,
            0, 0, 2, 24, sps, Pps(), new(), new(), new(),
            referencePictureList0: [new H264ReferencePicture(0, 0)]);

        Assert.Equal(0u, result.CodedBlockPattern.Luma);
        Assert.Equal(24, result.QpY);
        Assert.All(result.Reconstructed.Luma, sample => Assert.Equal((byte)100, sample));
        Assert.All(result.Reconstructed.Cb, sample => Assert.Equal((byte)110, sample));
        Assert.All(result.Reconstructed.Cr, sample => Assert.Equal((byte)120, sample));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        H264DeblockingMacroblockMetadata metadata =
            destination.GetDeblockingMetadata(0, 0)!;
        Assert.Equal(24, metadata.QpY);
        Assert.Equal(16, metadata.LumaBlocks.Count);
        Assert.Equal(new H264ReferencePicture(0, 0), metadata.LumaBlocks[0].ReferencePictureL0);
        Assert.Equal(new H264MotionVector(0, 0), metadata.LumaBlocks[0].MotionVectorL0);
        Assert.Null(metadata.LumaBlocks[0].ReferencePictureL1);
        Assert.Equal((byte)100, destination.GetLumaSample(0, 0));
        Assert.Equal((byte)110, destination.GetChromaSample(1, 0, 0));
    }

    [Fact]
    public void DecodeAndReconstruct_ShouldApplyMbQpDeltaAndParseCodedLumaGroup()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Surface(sps, 80, 90, 100);
        H264DecodedPictureSurface destination = new(sps);
        // MVDx=0, MVDy=0; inter CBP codeNum=2 (luma group 0);
        // mb_qp_delta=+1; four zero coeff_token values for the 4x4 blocks.
        H264InterMacroblockReconstructionResult result = _decoder.DecodeAndReconstruct(
            Bits("11" + "011" + "010" + "1111"),
            Header(), 0, [reference],
            new H264MotionVectorPictureState(1, 1), destination,
            0, 0, 3, 24, sps, Pps(), new(), new(), new());

        Assert.Equal(25, result.QpY);
        Assert.Equal(1u, result.CodedBlockPattern.Luma);
        Assert.Equal(0u, result.CodedBlockPattern.Chroma);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.Equal((byte)80, destination.GetLumaSample(0, 0));
    }

    [Fact]
    public void DecodeSkippedAndReconstruct_ShouldCommitPredictionWithoutResidualSyntax()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Surface(sps, 30, 40, 50);
        H264DecodedPictureSurface destination = new(sps);
        H264MacroblockHeader skipped = new(0, 0, true, 1,
            false, false, null, null, null, false);

        H264InterMacroblockReconstructionResult result =
            _decoder.DecodeSkippedAndReconstruct(
                skipped, [reference], new H264MotionVectorPictureState(1, 1),
                destination, 0, 0, 4, 27, sps,
                referencePictureList0: [new H264ReferencePicture(0, 0)]);

        Assert.Equal(27, result.QpY);
        Assert.Equal(0u, result.CodedBlockPattern.Luma);
        Assert.Equal((byte)30, destination.GetLumaSample(0, 0));
        Assert.Equal((byte)40, destination.GetChromaSample(1, 0, 0));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        H264DeblockingMacroblockMetadata metadata =
            destination.GetDeblockingMetadata(0, 0)!;
        Assert.Equal(27, metadata.QpY);
        Assert.Equal(new H264ReferencePicture(0, 0), metadata.LumaBlocks[0].ReferencePictureL0);
    }

    [Fact]
    public void DecodeAndReconstruct_ShouldRejectAlreadyDecodedDestinationBeforeReading()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Surface(sps, 1, 2, 3);
        H264DecodedPictureSurface destination = Surface(sps, 4, 5, 6);
        H264BitReader bits = Bits("111");

        Assert.Throws<InvalidOperationException>(() => _decoder.DecodeAndReconstruct(
            bits, Header(), 0, [reference],
            new H264MotionVectorPictureState(1, 1), destination,
            0, 0, 0, 24, sps, Pps(), new(), new(), new()));
        Assert.Equal(3, bits.RemainingBits);
    }


    [Fact]
    public void ReconstructCabac_WithNoResidual_ShouldCommitPredictionAndDeblockingMetadata()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader cabacPps = new(0, 0, true, false, 0);
        H264DecodedPictureSurface reference = Surface(sps, 100, 110, 120);
        H264DecodedPictureSurface destination = new(sps);
        H264MotionVectorPictureState motionState = new(1, 1);
        H264InterMacroblockSyntax syntax = new(
            0, false, false,
            [
                new H264InterPredictionPartition(0, 0, 16, 16, 0,
                    [new H264InterSubPartition(0, 0, 16, 16,
                        [new H264MotionVectorDifference(0, 0)])])
            ]);
        IReadOnlyList<H264InterMacroblockPrediction> predictions =
            new H264InterMacroblockPredictionDecoder().PredictFromSyntax(
                syntax, 0, [reference], motionState, 0, 0, 2);

        H264CodedBlockPattern pattern = new(0, 0, 0, sps.ChromaFormatIdc);
        H264InterMacroblockResidualSyntax residualSyntax = new(pattern, null);
        H264InterResidualMacroblock residual = EmptyCabacResidual(0);
        H264InterMacroblockReconstructionResult result = _decoder.ReconstructCabac(
            Header(), predictions, residualSyntax, residual,
            motionState, [new H264ReferencePicture(0, 0)], destination,
            0, 0, 2, 24, sps, cabacPps);

        Assert.Equal(24, result.QpY);
        Assert.Equal(0u, result.CodedBlockPattern.Luma);
        Assert.All(result.Reconstructed.Luma, value => Assert.Equal((byte)100, value));
        Assert.All(result.Reconstructed.Cb, value => Assert.Equal((byte)110, value));
        Assert.All(result.Reconstructed.Cr, value => Assert.Equal((byte)120, value));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        H264DeblockingMacroblockMetadata metadata =
            destination.GetDeblockingMetadata(0, 0)!;
        Assert.Equal(24, metadata.QpY);
        Assert.Equal(new H264ReferencePicture(0, 0), metadata.LumaBlocks[0].ReferencePictureL0);
    }

    [Fact]
    public void ReconstructCabac_ShouldApplyMbQpDelta()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader cabacPps = new(0, 0, true, false, 0);
        H264DecodedPictureSurface reference = Surface(sps, 90, 100, 110);
        H264DecodedPictureSurface destination = new(sps);
        H264MotionVectorPictureState motionState = new(1, 1);
        H264InterMacroblockSyntax syntax = new(
            0, false, false,
            [
                new H264InterPredictionPartition(0, 0, 16, 16, 0,
                    [new H264InterSubPartition(0, 0, 16, 16,
                        [new H264MotionVectorDifference(0, 0)])])
            ]);
        IReadOnlyList<H264InterMacroblockPrediction> predictions =
            new H264InterMacroblockPredictionDecoder().PredictFromSyntax(
                syntax, 0, [reference], motionState, 0, 0, 0);

        H264CodedBlockPattern pattern = new(0, 0, 0, sps.ChromaFormatIdc);
        H264InterMacroblockResidualSyntax residualSyntax = new(pattern, 1);
        H264InterMacroblockReconstructionResult result = _decoder.ReconstructCabac(
            Header(), predictions, residualSyntax, EmptyCabacResidual(0),
            motionState, [new H264ReferencePicture(0, 0)], destination,
            0, 0, 0, 24, sps, cabacPps);

        Assert.Equal(25, result.QpY);
        Assert.Equal(25, destination.GetDeblockingMetadata(0, 0)!.QpY);
    }

    [Fact]
    public void ReconstructCabac_ShouldRejectCavlcPpsBeforeWritingDestination()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Surface(sps, 1, 2, 3);
        H264DecodedPictureSurface destination = new(sps);
        H264MotionVectorPictureState motionState = new(1, 1);
        H264InterMacroblockSyntax syntax = new(
            0, false, false,
            [
                new H264InterPredictionPartition(0, 0, 16, 16, 0,
                    [new H264InterSubPartition(0, 0, 16, 16,
                        [new H264MotionVectorDifference(0, 0)])])
            ]);
        IReadOnlyList<H264InterMacroblockPrediction> predictions =
            new H264InterMacroblockPredictionDecoder().PredictFromSyntax(
                syntax, 0, [reference], motionState, 0, 0, 0);

        Assert.Throws<ArgumentException>(() => _decoder.ReconstructCabac(
            Header(), predictions,
            new H264InterMacroblockResidualSyntax(
                new H264CodedBlockPattern(0, 0, 0, sps.ChromaFormatIdc), null),
            EmptyCabacResidual(0), motionState,
            [new H264ReferencePicture(0, 0)], destination,
            0, 0, 0, 24, sps, Pps()));
        Assert.False(destination.IsMacroblockDecoded(0, 0));
    }

    private static H264InterResidualMacroblock EmptyCabacResidual(uint address)
    {
        H264CavlcResidualBlock Empty(int count) =>
            new(count, 0, 0, 0, new int[count]);
        H264IntraLumaResidualBlock[] luma = new H264IntraLumaResidualBlock[16];
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            luma[y * 4 + x] = new H264IntraLumaResidualBlock(
                x, y, false, Empty(16));
        H264IntraChromaResidualComponent[] chroma =
        [
            new(1, null, Enumerable.Range(0, 4)
                .Select(i => new H264IntraChromaResidualBlock(i % 2, i / 2, false, Empty(15))).ToArray()),
            new(2, null, Enumerable.Range(0, 4)
                .Select(i => new H264IntraChromaResidualBlock(i % 2, i / 2, false, Empty(15))).ToArray())
        ];
        return new H264InterResidualMacroblock(address, luma, chroma);
    }

    private static H264MacroblockHeader Header() =>
        new(0, 0, false, 0, false, false, null, null, null, false);

    private static H264SequenceParameterSet Sps() =>
        new(0, 100, 40, 1, false, 0, 0, 0, 0, 0, 1,
            0, 0, true, true, 0, 0, false);

    private static H264PictureParameterSetHeader Pps() =>
        new(0, 0, false, false, 0);

    private static H264DecodedPictureSurface Surface(
        H264SequenceParameterSet sps, byte y, byte cb, byte cr)
    {
        H264DecodedPictureSurface surface = new(sps);
        surface.WriteMacroblock(0, 0, 1, false,
            Enumerable.Repeat(y, 256).ToArray(),
            Enumerable.Repeat(cb, 64).ToArray(),
            Enumerable.Repeat(cr, 64).ToArray());
        return surface;
    }

    private static string WriteUe(uint value)
    {
        uint codeNum = checked(value + 1);
        int leadingZeroes = 0;
        for (uint current = codeNum; current > 1; current >>= 1)
            leadingZeroes++;
        return new string('0', leadingZeroes) +
            Convert.ToString(codeNum, 2).PadLeft(leadingZeroes + 1, '0');
    }

    private static H264BitReader Bits(string bits)
    {
        byte[] bytes = new byte[(bits.Length + 7) / 8];
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] == '1')
                bytes[i / 8] |= (byte)(1 << (7 - i % 8));
        return new H264BitReader(bytes, bits.Length);
    }
}

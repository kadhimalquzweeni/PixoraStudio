using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BInterSliceDataDecoderTests
{

    [Fact]
    public void Decode_CabacBSkipSlice_ShouldTraverseAndReconstructMacroblock()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader pps = Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 47);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        ScriptedCabacSliceDataReader cabac = new([1], [1]);
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1),
            new H264BInterMacroblockReconstructor(),
            (reader, qpY, isIntra, cabacInitIdc) =>
            {
                Assert.Equal(26, qpY);
                Assert.False(isIntra);
                Assert.Equal(0, cabacInitIdc);
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 0, 0,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(1, result.MacroblocksDecoded);
        H264BInterMacroblockReconstructionResult skipped =
            Assert.Single(result.InterMacroblocks);
        Assert.True(skipped.IsSkipped);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.Equal((byte)47, destination.GetLumaSample(0, 0));
        Assert.True(cabac.IsTerminated);
        Assert.Equal(new[] { 24 }, cabac.ContextIndices);
    }

    [Fact]
    public void Decode_CabacBSlice_ShouldCarrySkipContextFromInterMacroblock()
    {
        H264SequenceParameterSet sps = Sps(2);
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 45);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        // Decode B_L0_16x16 followed by B_Skip. The first macroblock is
        // non-skipped, so it raises the left-neighbour skip context increment.
        ScriptedCabacSliceDataReader cabac = new(
            [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1],
            [0, 1]);
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(2, 1),
            new H264MotionVectorPictureState(2, 1),
            new H264BInterMacroblockReconstructor(),
            (reader, _, _, _) =>
            {
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 0, 0,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(2, result.MacroblocksDecoded);
        Assert.Equal(2, result.InterMacroblocks.Count);
        Assert.False(result.InterMacroblocks[0].IsSkipped);
        Assert.True(result.InterMacroblocks[1].IsSkipped);
        Assert.All(Enumerable.Range(0, 2),
            x => Assert.True(destination.IsMacroblockDecoded(x, 0)));
        Assert.All(destination.CopyLumaPlane(),
            sample => Assert.Equal((byte)45, sample));
        Assert.Equal(
            new[] { 24, 27, 30, 32, 40, 47, 73, 74, 75, 76, 77, 25 },
            cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacBSlices_ShouldNotCarryNeighbourSkipContextAcrossSliceBoundary()
    {
        H264SequenceParameterSet sps = Sps(2);
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 45);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        ScriptedCabacSliceDataReader firstSliceCabac = new(
            [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0], [1]);
        ScriptedCabacSliceDataReader secondSliceCabac = new([1], [1]);
        Queue<IH264CabacSliceDataReader> readers = new(
            new IH264CabacSliceDataReader[] { firstSliceCabac, secondSliceCabac });
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(2, 1),
            new H264MotionVectorPictureState(2, 1),
            new H264BInterMacroblockReconstructor(),
            (_, _, _, _) => readers.Dequeue());

        BitWriter firstBits = WriteBSliceHeader(cabac: true, firstMacroblock: 0);
        firstBits.AlignWithOneBits();
        firstBits.Bits(0, 9);
        firstBits.TrailingBits();
        H264BInterSliceDecodeResult first = decoder.Decode(
            new H264NalUnit(0x41, firstBits.ToArray()), sps, pps, dpb, 14, 8,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(1, first.MacroblocksDecoded);
        Assert.Equal(24, firstSliceCabac.ContextIndices[0]);

        // The second slice starts at the adjacent macroblock but must not
        // treat the previous slice's coded macroblock as an available neighbour.
        BitWriter secondBits = WriteBSliceHeader(cabac: true, firstMacroblock: 1);
        secondBits.AlignWithOneBits();
        secondBits.Bits(0, 9);
        secondBits.TrailingBits();
        H264BInterSliceDecodeResult second = decoder.Decode(
            new H264NalUnit(0x41, secondBits.ToArray()), sps, pps, dpb, 15, 8,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(1, second.MacroblocksDecoded);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.IsMacroblockDecoded(1, 0));
        Assert.Equal(new[] { 24 }, secondSliceCabac.ContextIndices);
        Assert.True(secondSliceCabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacBInterSlice_ShouldDecodeL0MacroblockAndReconstruct()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader pps = Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 53);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        ScriptedCabacSliceDataReader cabac = new(
            [0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [1]);
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1),
            new H264BInterMacroblockReconstructor(),
            (reader, qpY, isIntra, cabacInitIdc) =>
            {
                Assert.Equal(26, qpY);
                Assert.False(isIntra);
                Assert.Equal(0, cabacInitIdc);
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 0, 0,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(1, result.MacroblocksDecoded);
        H264BInterMacroblockReconstructionResult inter =
            Assert.Single(result.InterMacroblocks);
        Assert.False(inter.IsSkipped);
        Assert.Equal(26, inter.QpY);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.Equal((byte)53, destination.GetLumaSample(0, 0));
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacBDirectInterSlice_ShouldReconstructSpatialDirectPrediction()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 61);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        // mb_skip_flag=0; B_Direct_16x16; zero luma/chroma CBP;
        // end_of_slice_flag=1. Direct prediction has no ref_idx or MVD bins.
        ScriptedCabacSliceDataReader cabac = new(
            [0, 0, 0, 0, 0, 0, 0],
            [1]);
        H264BInterMacroblockReconstructor reconstructor = new();
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1),
            reconstructor,
            (reader, qpY, isIntra, cabacInitIdc) =>
            {
                Assert.Equal(26, qpY);
                Assert.False(isIntra);
                Assert.Equal(0, cabacInitIdc);
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 2, 6,
            collocatedContextProvider: (_, _, _, _) => null);

        H264BInterMacroblockReconstructionResult macroblock =
            Assert.Single(result.InterMacroblocks);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.False(macroblock.IsSkipped);
        Assert.Single(result.ReferencePictureLists!.List0);
        Assert.Single(result.ReferencePictureLists.List1);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.All(destination.CopyLumaPlane(),
            sample => Assert.Equal((byte)61, sample));
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks, block =>
        {
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL0);
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL1);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL0);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL1);
        });
        Assert.Equal(new[] { 24, 27, 73, 74, 75, 76, 77 }, cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacBDirectInterSlice_WithNonZeroLumaResidual_ShouldReconstructResidual()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader pps = Pps() with
        {
            EntropyCodingModeFlag = true,
            Transform8X8ModeFlag = true
        };
        H264DecodedPictureSurface reference = Reference(sps, 61);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        // B_Direct_16x16, luma CBP group 0, QP delta +1 and one
        // positive non-zero 4x4 luma DC coefficient.
        ScriptedCabacSliceDataReader cabac = new(
            [
                0,       // mb_skip_flag
                0,       // B_Direct_16x16 mb_type
                1, 0, 0, 0, 0, // coded_block_pattern
                0,       // transform_size_8x8_flag
                1, 0,    // mb_qp_delta = +1
                1, 1, 1, 0, 0, 0, 0 // residual CBF/significance/level/CBFs
            ],
            [1],
            [0]);
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1),
            new H264BInterMacroblockReconstructor(),
            (_, _, _, _) => cabac);

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 13, 8,
            collocatedContextProvider: (_, _, _, _) => null);

        H264BInterMacroblockReconstructionResult macroblock =
            Assert.Single(result.InterMacroblocks);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Equal(27, macroblock.QpY);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.Contains(destination.CopyLumaPlane(), sample => sample != 61);
        Assert.All(destination.CopyCbPlane(), sample => Assert.Equal((byte)61, sample));
        Assert.All(destination.CopyCrPlane(), sample => Assert.Equal((byte)61, sample));
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks, block =>
        {
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL0);
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL1);
        });
        Assert.Equal(
            new[]
            {
                24, 27,
                73, 73, 73, 76, 77, 399, 60, 62,
                93, 134, 195, 248, 94, 95, 93
            },
            cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacBDirect8x8Macroblock_ShouldReconstructFourSpatialDirectRegions()
    {
        H264SequenceParameterSet sps = Sps() with { Direct8X8InferenceFlag = true };
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 62);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        // mb_skip_flag=0; mb_type=22 (B_8x8); four direct sub_mb_type=0;
        // zero coded_block_pattern; end_of_slice_flag=1.
        ScriptedCabacSliceDataReader cabac = new(
            [
                0,             // mb_skip_flag
                1, 1, 1, 1, 1, 1, // B_8x8 macroblock type
                0, 0, 0, 0,    // four direct 8x8 sub-macroblock types
                0, 0, 0, 0, 0  // zero luma/chroma coded_block_pattern
            ],
            [1]);
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1),
            new H264BInterMacroblockReconstructor(),
            (reader, _, _, _) =>
            {
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 2, 7,
            collocatedContextProvider: (_, _, _, _) => null);

        H264BInterMacroblockReconstructionResult macroblock =
            Assert.Single(result.InterMacroblocks);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.False(macroblock.IsSkipped);
        Assert.Equal(4, macroblock.Partitions.Count);
        Assert.All(macroblock.Partitions, partition =>
        {
            Assert.Equal(2, partition.Width);
            Assert.Equal(2, partition.Height);
            Assert.Equal(0, partition.ReferenceIndexL0);
            Assert.Equal(0, partition.ReferenceIndexL1);
            Assert.Equal(new H264MotionVector(0, 0), partition.MotionVectorL0);
            Assert.Equal(new H264MotionVector(0, 0), partition.MotionVectorL1);
        });
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.All(destination.CopyLumaPlane(),
            sample => Assert.Equal((byte)62, sample));
        Assert.Equal(
            new[] { 24, 27, 30, 32, 32, 32, 32, 36, 36, 36, 36, 73, 74, 75, 76, 77 },
            cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacBList1InterSlice_ShouldReconstructUsingList1Reference()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 59);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(directSpatial: false, cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        // mb_skip_flag=0; B_L1_16x16; zero list-1 MVD; zero CBP;
        // end_of_slice_flag=1.
        ScriptedCabacSliceDataReader cabac = new(
            [0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0],
            [1]);
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1),
            new H264BInterMacroblockReconstructor(),
            (reader, qpY, isIntra, cabacInitIdc) =>
            {
                Assert.Equal(26, qpY);
                Assert.False(isIntra);
                Assert.Equal(0, cabacInitIdc);
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 2, 4,
            collocatedContextProvider: (_, _, _, _) => null);

        H264BInterMacroblockReconstructionResult macroblock =
            Assert.Single(result.InterMacroblocks);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.False(macroblock.IsSkipped);
        Assert.Single(result.ReferencePictureLists!.List1);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)59, sample));
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks, block =>
        {
            Assert.Null(block.ReferencePictureL0);
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL1);
            Assert.Null(block.MotionVectorL0);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL1);
        });
        Assert.Equal(new[] { 24, 27, 30, 31, 32, 32, 32, 40, 47, 73, 74, 75, 76, 77 },
            cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacBBiDirectionalInterSlice_ShouldReconstructBothReferenceLists()
    {
        H264SequenceParameterSet sps = Sps();
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 63);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(directSpatial: false, cabac: true);
        bits.AlignWithOneBits();
        bits.Bits(0, 9);
        bits.TrailingBits();

        // mb_skip_flag=0; B_Bi_16x16; zero MVD for both lists;
        // zero coded_block_pattern; end_of_slice_flag=1.
        ScriptedCabacSliceDataReader cabac = new(
            [0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [1]);
        H264BInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264MotionVectorPictureState(1, 1),
            new H264BInterMacroblockReconstructor(),
            (reader, _, _, _) =>
            {
                while (!reader.IsByteAligned)
                    Assert.True(reader.ReadFlag());
                _ = reader.ReadBits(9);
                return cabac;
            });

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps, dpb, 2, 5,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Single(result.InterMacroblocks);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Single(result.ReferencePictureLists!.List0);
        Assert.Single(result.ReferencePictureLists.List1);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)63, sample));
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks, block =>
        {
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL0);
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL1);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL0);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL1);
        });
        Assert.Equal(
            new[] { 24, 27, 30, 31, 32, 32, 32, 40, 47, 40, 47, 73, 74, 75, 76, 77 },
            cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_BSlice_ShouldTraverseAndCommitInterMacroblock()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Reference(sps, 63);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        H264BInterSliceDataDecoder decoder = Decoder(destination);
        BitWriter bits = WriteBSliceHeader();
        bits.Ue(0); // mb_skip_run
        bits.Ue(1); // B_L0_16x16
        bits.Se(0); bits.Se(0); // list-0 MVD
        bits.Ue(0); // coded_block_pattern = 0
        bits.TrailingBits();

        H264BInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(H264SliceType.B, result.SliceHeader.SliceType);
        Assert.Single(result.ReferencePictureLists!.List0);
        Assert.Single(result.ReferencePictureLists.List1);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Single(result.InterMacroblocks);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks, block =>
        {
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL0);
            Assert.Null(block.ReferencePictureL1);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL0);
            Assert.Null(block.MotionVectorL1);
        });
        Assert.Equal((byte)63, destination.GetLumaSample(0, 0));
    }

    [Fact]
    public void Decode_BSkipRun_ShouldReconstructEachSkippedMacroblock()
    {
        H264SequenceParameterSet sps = Sps(2);
        H264DecodedPictureSurface reference = Reference(sps, 41);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader();
        bits.Ue(2); // two B_Skip macroblocks
        bits.TrailingBits();

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(2, result.MacroblocksDecoded);
        Assert.Equal(2, result.InterMacroblocks.Count);
        Assert.All(Enumerable.Range(0, 2), x => Assert.True(destination.IsMacroblockDecoded(x, 0)));
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)41, sample));
    }


    [Fact]
    public void Decode_BSkipRunBeforeInterMacroblock_ShouldSeedZeroCavlcNeighbourContexts()
    {
        H264SequenceParameterSet sps = Sps(2);
        H264DecodedPictureSurface reference = Reference(sps, 41);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader();
        bits.Ue(1); // one B_Skip macroblock
        bits.Ue(0); // next mb_skip_run is zero
        bits.Ue(1); // B_L0_16x16
        bits.Se(0); bits.Se(0);
        bits.Ue(2); // coded_block_pattern: luma group 0
        bits.Se(0); // mb_qp_delta
        for (int i = 0; i < 4; i++) bits.Flag(true); // four zero coeff_token values
        bits.TrailingBits();

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0,
            collocatedContextProvider: (_, _, _, _) => null);

        Assert.Equal(2, result.MacroblocksDecoded);
        Assert.Equal(2, result.InterMacroblocks.Count);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.IsMacroblockDecoded(1, 0));
    }

    [Fact]
    public void Decode_ExplicitBInterWithoutDirectPartitions_ShouldNotRequireCollocatedMotionMetadata()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Reference(sps, 55);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(directSpatial: false);
        bits.Ue(0); // mb_skip_run
        bits.Ue(1); // B_L0_16x16
        bits.Se(0); bits.Se(0);
        bits.Ue(0); // coded_block_pattern = 0
        bits.TrailingBits();

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0);

        Assert.Single(result.InterMacroblocks);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void Decode_BSkipRun_TemporalDirect_ShouldUseCollocatedMotionProvider()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Reference(sps, 41);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader(directSpatial: false);
        bits.Ue(1); // one B_Skip macroblock
        bits.TrailingBits();

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0,
            (_, _, _, _) => new H264TemporalDirectPredictionContext(
                new H264MotionVector(2, -2), 0, 0, 0, false));

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.True(Assert.Single(result.InterMacroblocks).IsSkipped);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.Equal((byte)41, destination.GetLumaSample(0, 0));
    }

    [Fact]
    public void Decode_BSkipRun_TemporalDirect_ShouldResolveCollocatedMotionFromDpbAutomatically()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface collocated = Reference(sps, 41);
        H264DecodedPictureSurface pastReference = Reference(sps, 42);
        H264DecodedPictureSurface secondPastReference = Reference(sps, 43);
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(
            pastReference, new H264ReferencePicture(15, 1), true, 4, 15, 16,
            new H264DecodedReferenceMarking(false, false, false, []));
        dpb.StoreDecodedPicture(
            secondPastReference, new H264ReferencePicture(0, 0), true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, false, false, []));
        H264MotionVectorPictureState collocatedL0 = new(1, 1);
        H264MotionVectorPictureState collocatedL1 = new(1, 1);
        collocatedL0.RecordInterPartition(0, 0, 4, 4, 0, 0, new H264MotionVector(2, -2));
        dpb.StoreDecodedPicture(
            collocated, new H264ReferencePicture(2, 4), true, 4, 2, 16,
            new H264DecodedReferenceMarking(false, false, false, []),
            motionVectorStateL0: collocatedL0,
            motionVectorStateL1: collocatedL1,
            referenceList0: [new H264ReferencePicture(0, 0)],
            referenceList1: []);

        BitWriter bits = WriteBSliceHeader(
            directSpatial: false, overrideReferenceCounts: true,
            numRefIdxL0ActiveMinus1: 1);
        bits.Ue(1);
        bits.TrailingBits();
        H264DecodedPictureSurface destination = new(sps);

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 2, 0);

        H264BInterMacroblockReconstructionResult skipped =
            Assert.Single(result.InterMacroblocks);
        Assert.True(skipped.IsSkipped);
        Assert.Equal(2, result.ReferencePictureLists!.List0.Count);
        Assert.Single(result.ReferencePictureLists.List1);
        H264ReconstructedBInterSubPartition first = skipped.Partitions[0];
        Assert.Equal((int?)1, first.ReferenceIndexL0);
        Assert.Equal(new H264MotionVector(1, -1), first.MotionVectorL0);
        Assert.Equal(new H264MotionVector(-1, 1), first.MotionVectorL1);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void Decode_BSliceWithIntraNxNMacroblock_ShouldUseSharedIntraReconstruction()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Reference(sps, 33);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader();
        bits.Ue(0); // mb_skip_run
        bits.Ue(23); // B-slice I_NxN macroblock
        for (int i = 0; i < 16; i++) bits.Flag(true); // prev_intra4x4_pred_mode_flag
        bits.Ue(0); // intra_chroma_pred_mode
        bits.Ue(3); // intra coded_block_pattern maps to zero
        bits.TrailingBits();

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Empty(result.InterMacroblocks);
        Assert.Equal(new uint[] { 0 }, result.IntraMacroblockAddresses);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
    }

    [Fact]
    public void Decode_BSliceWithIpcmMacroblock_ShouldReadRawSamples()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Reference(sps, 33);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteBSliceHeader();
        bits.Ue(0); // mb_skip_run
        bits.Ue(48); // B-slice I_PCM macroblock type
        bits.AlignToByte();
        for (int i = 0; i < 256; i++) bits.Bits(99, 8);
        for (int i = 0; i < 64; i++) bits.Bits(109, 8);
        for (int i = 0; i < 64; i++) bits.Bits(119, 8);
        bits.TrailingBits();

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Empty(result.InterMacroblocks);
        Assert.Equal(new uint[] { 0 }, result.IntraMacroblockAddresses);
        Assert.Equal((byte)99, destination.GetLumaSample(0, 0));
        Assert.Equal((byte)109, destination.GetChromaSample(1, 0, 0));
        Assert.Equal((byte)119, destination.GetChromaSample(2, 0, 0));
        H264DeblockingMacroblockMetadata metadata =
            destination.GetDeblockingMetadata(0, 0)!;
        Assert.Equal(0, metadata.QpY);
        Assert.Equal(0, metadata.QpCb);
        Assert.Equal(0, metadata.QpCr);
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks,
            block => Assert.True(block.IsIntra));
    }

    [Fact]
    public void Decode_ShouldRejectNonBAndUnsupportedConfiguration()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Reference(sps, 1);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        BitWriter p = WritePSliceHeader();
        p.TrailingBits();
        Assert.Throws<NotSupportedException>(() => Decoder(new H264DecodedPictureSurface(sps)).Decode(
            new H264NalUnit(0x41, p.ToArray()), sps, Pps(), dpb, 0, 0));
        Assert.Throws<NotSupportedException>(() => Decoder(new H264DecodedPictureSurface(sps)).Decode(
            new H264NalUnit(0x41, WriteBSliceHeader().ToArray()), sps,
            Pps() with { NumSliceGroupsMinus1 = 1 }, dpb, 0, 0));
    }


    private sealed class ScriptedCabacSliceDataReader : IH264CabacSliceDataReader
    {
        private readonly Queue<int> _decisionBins;
        private readonly Queue<int> _terminateBins;
        private readonly Queue<int> _bypassBins;

        public ScriptedCabacSliceDataReader(
            IEnumerable<int> decisionBins,
            IEnumerable<int> terminateBins,
            IEnumerable<int>? bypassBins = null)
        {
            _decisionBins = new Queue<int>(decisionBins);
            _terminateBins = new Queue<int>(terminateBins);
            _bypassBins = new Queue<int>(bypassBins ?? []);
        }

        public List<int> ContextIndices { get; } = [];
        public bool IsTerminated { get; private set; }

        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _decisionBins.Dequeue();
        }

        public int DecodeBypass() => _bypassBins.Dequeue();

        public int DecodeTerminate() => _terminateBins.Dequeue();
        public void MarkTerminated() => IsTerminated = true;
        public void RestartAfterPcm() =>
            throw new InvalidOperationException("These B-slice tests do not contain PCM.");
    }

    private static H264BInterSliceDataDecoder Decoder(H264DecodedPictureSurface surface) =>
        new(surface, new H264MotionVectorPictureState(surface.MacroblockWidth, surface.MacroblockHeight),
            new H264MotionVectorPictureState(surface.MacroblockWidth, surface.MacroblockHeight));

    private static H264SequenceParameterSet Sps(int width = 1) => new(
        0, 66, 30, 1, false, 0, 0, 0, 0, 0, 2,
        (uint)(width - 1), 0, true, true, (uint)(width * 16), 16, false);

    private static H264PictureParameterSetHeader Pps() => new(0, 0, false, false, 0);

    private static H264DecodedPictureSurface Reference(H264SequenceParameterSet sps, byte value)
    {
        H264DecodedPictureSurface surface = new(sps);
        for (int x = 0; x < surface.MacroblockWidth; x++)
        {
            surface.WriteMacroblock(x, 0, 0, false,
                Enumerable.Repeat(value, 256).ToArray(),
                Enumerable.Repeat(value, 64).ToArray(),
                Enumerable.Repeat(value, 64).ToArray());
            surface.SetDeblockingMetadata(x, 0, ReferenceDeblockingMetadata());
        }
        return surface;
    }

    private static H264DeblockingMacroblockMetadata ReferenceDeblockingMetadata()
    {
        H264DeblockingBlockMetadata inter = new(false, false, null, null, null, null);
        return new H264DeblockingMacroblockMetadata(
            26, 26, 26, 1, 0, 0, false,
            Enumerable.Repeat(inter, 16).ToArray(),
            Enumerable.Repeat(inter, 4).ToArray(),
            Enumerable.Repeat(inter, 4).ToArray());
    }

    private static H264DecodedPictureBuffer Dpb(H264DecodedPictureSurface surface)
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(surface, new H264ReferencePicture(0, 0),
            true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, false, false, []));
        return dpb;
    }

    private static BitWriter WritePSliceHeader()
    {
        BitWriter bits = new();
        bits.Ue(0); // first_mb_in_slice
        bits.Ue(0); // P slice
        bits.Ue(0); // pic_parameter_set_id
        bits.Bits(1, 4); // frame_num
        bits.Bits(0, 4); // pic_order_cnt_lsb
        bits.Flag(false); // num_ref_idx_active_override_flag
        bits.Flag(false); // ref_pic_list_modification_flag_l0
        bits.Flag(false); // adaptive_ref_pic_marking_mode_flag
        bits.Se(0); // slice_qp_delta
        return bits;
    }

    private static BitWriter WriteBSliceHeader(
        uint sliceType = 1, bool directSpatial = true,
        bool overrideReferenceCounts = false, uint numRefIdxL0ActiveMinus1 = 0,
        bool cabac = false, uint firstMacroblock = 0)
    {
        BitWriter bits = new();
        bits.Ue(firstMacroblock); // first_mb_in_slice
        bits.Ue(sliceType);
        bits.Ue(0); // pic_parameter_set_id
        bits.Bits(1, 4); // frame_num
        bits.Bits(0, 4); // pic_order_cnt_lsb
        bits.Flag(directSpatial); // direct_spatial_mv_pred_flag
        bits.Flag(overrideReferenceCounts); // num_ref_idx_active_override_flag
        if (overrideReferenceCounts)
        {
            bits.Ue(numRefIdxL0ActiveMinus1);
            bits.Ue(0); // num_ref_idx_l1_active_minus1
        }
        bits.Flag(false); // ref_pic_list_modification_flag_l0
        bits.Flag(false); // ref_pic_list_modification_flag_l1
        bits.Flag(false); // adaptive_ref_pic_marking_mode_flag
        if (cabac) bits.Ue(0); // cabac_init_idc
        bits.Se(0); // slice_qp_delta
        return bits;
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];
        public void Flag(bool value) => _bits.Add(value);
        public void AlignToByte()
        {
            while ((_bits.Count & 7) != 0) Flag(false);
        }
        public void AlignWithOneBits()
        {
            while ((_bits.Count & 7) != 0) Flag(true);
        }
        public void Bits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--) _bits.Add(((value >> i) & 1) != 0);
        }
        public void Ue(uint value)
        {
            uint code = value + 1; int zeros = 0;
            for (uint n = code; n > 1; n >>= 1) zeros++;
            for (int i = 0; i < zeros; i++) _bits.Add(false);
            Bits(code, zeros + 1);
        }
        public void Se(int value) => Ue(value <= 0
            ? checked((uint)(-2L * value))
            : checked((uint)(2L * value - 1)));
        public void TrailingBits()
        {
            Flag(true);
            while ((_bits.Count & 7) != 0) Flag(false);
        }
        public byte[] ToArray()
        {
            byte[] result = new byte[(_bits.Count + 7) / 8];
            for (int i = 0; i < _bits.Count; i++)
                if (_bits[i]) result[i / 8] |= (byte)(1 << (7 - i % 8));
            return result;
        }
    }
}

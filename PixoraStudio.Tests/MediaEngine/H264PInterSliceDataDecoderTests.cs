using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264PInterSliceDataDecoderTests
{

    [Fact]
    public void Decode_CabacPSkipSlice_ShouldReconstructAndCommitMacroblock()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 70, 80, 90);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteCabacPSliceHeader();
        bits.AlignWithOneBits();
        bits.WriteBits(0, 9);
        bits.WriteRbspTrailingBits();

        ScriptedCabacSliceDataReader cabac = new([1], [1]);
        H264PInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264PInterMacroblockReconstructor(),
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

        H264PInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps,
            [reference], 4, [new H264ReferencePicture(0, 0)]);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Equal(26, result.FinalQpY);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)70, sample));
        Assert.All(destination.CopyCbPlane(), sample => Assert.Equal((byte)80, sample));
        Assert.All(destination.CopyCrPlane(), sample => Assert.Equal((byte)90, sample));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.Equal(new[] { 11 }, cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_CabacPInterSlice_ShouldDecodeInterMacroblockAndReconstructReferencePrediction()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264PictureParameterSetHeader pps =
            Pps() with { EntropyCodingModeFlag = true };
        H264DecodedPictureSurface reference = Reference(sps, 70, 80, 90);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WriteCabacPSliceHeader();
        bits.AlignWithOneBits();
        bits.WriteBits(0, 9);
        bits.WriteRbspTrailingBits();

        // mb_skip_flag=0; P_L0_16x16; zero horizontal/vertical MVD;
        // zero luma/chroma coded_block_pattern; end_of_slice_flag=1.
        ScriptedCabacSliceDataReader cabac = new(
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [1]);
        H264PInterSliceDataDecoder decoder = new(
            destination,
            new H264MotionVectorPictureState(1, 1),
            new H264PInterMacroblockReconstructor(),
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

        H264PInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, pps,
            [reference], 7, [new H264ReferencePicture(0, 0)]);

        H264InterMacroblockReconstructionResult macroblock =
            Assert.Single(result.Macroblocks);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Equal(26, result.FinalQpY);
        Assert.False(macroblock.IsSkipped);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)70, sample));
        Assert.All(destination.CopyCbPlane(), sample => Assert.Equal((byte)80, sample));
        Assert.All(destination.CopyCrPlane(), sample => Assert.Equal((byte)90, sample));
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks, block =>
        {
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL0);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL0);
            Assert.Null(block.ReferencePictureL1);
        });
        Assert.Equal(new[] { 11, 14, 15, 16, 40, 47, 73, 74, 75, 76 },
            cabac.ContextIndices);
        Assert.True(cabac.IsTerminated);
    }

    [Fact]
    public void Decode_PInterSlice_ShouldTraverseAndCommitMacroblock()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264DecodedPictureSurface reference = Reference(sps, 70, 80, 90);
        H264DecodedPictureSurface destination = new(sps);
        H264PInterSliceDataDecoder decoder = new(
            destination, new H264MotionVectorPictureState(1, 1));
        BitWriter bits = WritePSliceHeader();
        bits.WriteUe(0); // mb_skip_run = 0
        bits.WriteUe(0); // P_L0_16x16
        bits.WriteSe(0); // mvd_l0[0][0]
        bits.WriteSe(0); // mvd_l0[0][1]
        bits.WriteUe(0); // inter coded_block_pattern = 0
        bits.WriteRbspTrailingBits();

        H264PInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), [reference], 2);

        Assert.Equal(H264SliceType.P, result.SliceHeader.SliceType);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)70, sample));
        Assert.All(destination.CopyCbPlane(), sample => Assert.Equal((byte)80, sample));
        Assert.All(destination.CopyCrPlane(), sample => Assert.Equal((byte)90, sample));
    }

    [Fact]
    public void Decode_PInterSlice_WithInter8x8Transform_ShouldDecodeAndCommitResidualLayout()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264DecodedPictureSurface reference = Reference(sps, 70, 80, 90);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WritePSliceHeader();
        bits.WriteUe(0); // mb_skip_run
        bits.WriteUe(0); // P_L0_16x16
        bits.WriteSe(0); bits.WriteSe(0); // motion-vector difference
        bits.WriteUe(2); // coded_block_pattern: luma group 0
        bits.WriteFlag(true); // transform_size_8x8_flag
        bits.WriteSe(0); // mb_qp_delta
        for (int i = 0; i < 4; i++) bits.WriteFlag(true); // four zero coeff_token values
        bits.WriteRbspTrailingBits();

        H264PInterSliceDecodeResult result = new H264PInterSliceDataDecoder(
            destination, new H264MotionVectorPictureState(1, 1)).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps,
            Pps() with { Transform8X8ModeFlag = true }, [reference], 2);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)70, sample));
    }

    [Fact]
    public void Decode_PSliceWithIpcmMacroblock_ShouldReadAndCommitRawSamples()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264DecodedPictureSurface reference = Reference(sps, 1, 2, 3);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WritePSliceHeader();
        bits.WriteUe(0); // mb_skip_run
        bits.WriteUe(30); // P-slice I_PCM macroblock type
        bits.AlignToByte();
        for (int i = 0; i < 256; i++) bits.WriteBits(101, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(111, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(121, 8);
        bits.WriteRbspTrailingBits();

        H264PInterSliceDecodeResult result = new H264PInterSliceDataDecoder(
            destination, new H264MotionVectorPictureState(1, 1)).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), [reference], 2);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Empty(result.Macroblocks);
        Assert.Equal(new uint[] { 0 }, result.IntraMacroblockAddresses);
        Assert.Equal((byte)101, destination.GetLumaSample(0, 0));
        Assert.Equal((byte)111, destination.GetChromaSample(1, 0, 0));
        Assert.Equal((byte)121, destination.GetChromaSample(2, 0, 0));
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
    public void Decode_PSkipRun_ShouldReconstructEverySkippedMacroblock()
    {
        H264SequenceParameterSet sps = Sps(2);
        H264DecodedPictureSurface reference = Reference(sps, 21, 31, 41);
        H264DecodedPictureSurface destination = new(sps);
        H264PInterSliceDataDecoder decoder = new(
            destination, new H264MotionVectorPictureState(2, 1));
        BitWriter bits = WritePSliceHeader();
        bits.WriteUe(2); // mb_skip_run = 2
        bits.WriteRbspTrailingBits();

        H264PInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), [reference], 1);

        Assert.Equal(2, result.MacroblocksDecoded);
        Assert.All(Enumerable.Range(0, 2), x => Assert.True(destination.IsMacroblockDecoded(x, 0)));
        Assert.All(destination.CopyLumaPlane(), sample => Assert.Equal((byte)21, sample));
    }

    [Fact]
    public void Decode_SkipRunBeforeInterMacroblock_ShouldSeedZeroCavlcNeighbourContexts()
    {
        H264SequenceParameterSet sps = Sps(2);
        H264DecodedPictureSurface reference = Reference(sps, 70, 80, 90);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter bits = WritePSliceHeader();
        bits.WriteUe(1); // one P_Skip macroblock
        bits.WriteUe(0); // next mb_skip_run is zero
        bits.WriteUe(0); // P_L0_16x16
        bits.WriteSe(0); bits.WriteSe(0);
        bits.WriteUe(2); // coded_block_pattern: top-left luma group
        bits.WriteSe(0); // mb_qp_delta
        for (int i = 0; i < 4; i++) bits.WriteFlag(true); // four zero coeff_token values
        bits.WriteRbspTrailingBits();

        H264PInterSliceDecodeResult result = new H264PInterSliceDataDecoder(
            destination, new H264MotionVectorPictureState(2, 1)).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), [reference], 0);

        Assert.Equal(2, result.MacroblocksDecoded);
        Assert.Equal(new uint[] { 0, 1 },
            result.Macroblocks.Select(macroblock => macroblock.MacroblockAddress));
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.IsMacroblockDecoded(1, 0));
    }

    [Fact]
    public void Decode_WithDpb_ShouldBuildRefPicList0AndReconstructPInterMacroblock()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264DecodedPictureSurface reference = Reference(sps, 61, 71, 81);
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(reference, new H264ReferencePicture(0, 0),
            true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, false, false, []));
        H264DecodedPictureSurface destination = new(sps);
        H264PInterSliceDataDecoder decoder = new(
            destination, new H264MotionVectorPictureState(1, 1));
        BitWriter bits = WritePSliceHeader(frameNum: 1);
        bits.WriteUe(0); // mb_skip_run = 0
        bits.WriteUe(0); // P_L0_16x16
        bits.WriteSe(0);
        bits.WriteSe(0);
        bits.WriteUe(0); // coded_block_pattern = 0
        bits.WriteRbspTrailingBits();

        H264PInterSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 9);

        Assert.Equal(1u, result.SliceHeader.FrameNum);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Equal(new H264ReferencePicture(0, 0), Assert.Single(result.ReferenceList0!));
        Assert.Equal((byte)61, destination.GetLumaSample(0, 0));
        Assert.Equal((byte)71, destination.GetChromaSample(1, 0, 0));
        Assert.Equal((byte)81, destination.GetChromaSample(2, 0, 0));
        Assert.True(destination.HasCompleteDeblockingMetadata);
        Assert.All(destination.GetDeblockingMetadata(0, 0)!.LumaBlocks, block =>
        {
            Assert.Equal(new H264ReferencePicture(0, 0), block.ReferencePictureL0);
            Assert.Equal(new H264MotionVector(0, 0), block.MotionVectorL0);
            Assert.Null(block.ReferencePictureL1);
        });
    }

    [Fact]
    public void Decode_PSliceWithIntraNxNMacroblock_ShouldReconstructAndCommitIt()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264DecodedPictureSurface reference = Reference(sps, 1, 2, 3);
        H264DecodedPictureSurface destination = new(sps);
        BitWriter intra = WritePSliceHeader();
        intra.WriteUe(0); // mb_skip_run
        intra.WriteUe(5); // P-slice I_NxN macroblock type
        for (int i = 0; i < 16; i++) intra.WriteFlag(true);
        intra.WriteUe(0); // intra_chroma_pred_mode
        intra.WriteUe(3); // coded_block_pattern maps to zero
        intra.WriteRbspTrailingBits();

        H264PInterSliceDecodeResult result = new H264PInterSliceDataDecoder(
            destination, new H264MotionVectorPictureState(1, 1)).Decode(
            new H264NalUnit(0x41, intra.ToArray()), sps, Pps(), [reference], 0);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Empty(result.Macroblocks);
        Assert.Equal(new uint[] { 0 }, result.IntraMacroblockAddresses);
        Assert.True(destination.IsMacroblockDecoded(0, 0));

        BitWriter p = WritePSliceHeader();
        p.WriteUe(0);
        p.WriteUe(0);
        p.WriteSe(0);
        p.WriteSe(0);
        p.WriteUe(0);
        p.WriteRbspTrailingBits();
        Assert.Throws<ArgumentException>(() =>
            new H264PInterSliceDataDecoder(new H264DecodedPictureSurface(sps),
                new H264MotionVectorPictureState(1, 1)).Decode(
                new H264NalUnit(0x41, p.ToArray()), sps, Pps(), [], 0));
    }

    [Fact]
    public void Decode_ShouldRejectNonPSlicesAndUnsupportedFmo()
    {
        H264SequenceParameterSet sps = Sps(1);
        BitWriter iSlice = WriteISliceHeader();
        iSlice.WriteRbspTrailingBits();
        Assert.Throws<NotSupportedException>(() =>
            new H264PInterSliceDataDecoder(new H264DecodedPictureSurface(sps),
                new H264MotionVectorPictureState(1, 1)).Decode(
                new H264NalUnit(0x65, iSlice.ToArray()), sps, Pps(),
                [Reference(sps, 1, 2, 3)], 0));

        BitWriter pSlice = WritePSliceHeader();
        pSlice.WriteUe(1);
        pSlice.WriteRbspTrailingBits();
        Assert.Throws<NotSupportedException>(() =>
            new H264PInterSliceDataDecoder(new H264DecodedPictureSurface(sps),
                new H264MotionVectorPictureState(1, 1)).Decode(
                new H264NalUnit(0x41, pSlice.ToArray()), sps,
                Pps() with { NumSliceGroupsMinus1 = 1 },
                [Reference(sps, 1, 2, 3)], 0));
    }

    [Fact]
    public void ResetPictureContexts_ShouldBeSafeForNewPicture()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264PInterSliceDataDecoder decoder = new(
            new H264DecodedPictureSurface(sps),
            new H264MotionVectorPictureState(1, 1));
        decoder.ResetPictureContexts();
        decoder.ResetPictureContexts();
    }

    private static BitWriter WriteCabacPSliceHeader()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(0); // P slice
        bits.WriteUe(0); // PPS id
        bits.WriteBits(0, 4); // frame_num
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteUe(0); // cabac_init_idc
        bits.WriteSe(0); // slice_qp_delta
        return bits;
    }

    private sealed class ScriptedCabacSliceDataReader : IH264CabacSliceDataReader
    {
        private readonly Queue<int> _decisionBins;
        private readonly Queue<int> _terminateBins;

        public ScriptedCabacSliceDataReader(
            IEnumerable<int> decisionBins, IEnumerable<int> terminateBins)
        {
            _decisionBins = new Queue<int>(decisionBins);
            _terminateBins = new Queue<int>(terminateBins);
        }

        public List<int> ContextIndices { get; } = [];
        public bool IsTerminated { get; private set; }

        public int DecodeBin(int contextIndex)
        {
            ContextIndices.Add(contextIndex);
            return _decisionBins.Dequeue();
        }

        public int DecodeBypass() =>
            throw new InvalidOperationException("The P_Skip test does not use bypass bins.");

        public int DecodeTerminate() => _terminateBins.Dequeue();
        public void MarkTerminated() => IsTerminated = true;
        public void RestartAfterPcm() =>
            throw new InvalidOperationException("The P_Skip test does not contain PCM.");
    }

    private static H264SequenceParameterSet Sps(int width) =>
        new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            (uint)(width - 1), 0, true, true, (uint)(width * 16), 16, false);

    private static H264PictureParameterSetHeader Pps() =>
        new(0, 0, false, false, 0);

    private static H264DecodedPictureSurface Reference(
        H264SequenceParameterSet sps, byte y, byte cb, byte cr)
    {
        H264DecodedPictureSurface surface = new(sps);
        for (int x = 0; x < surface.MacroblockWidth; x++)
        {
            surface.WriteMacroblock(x, 0, 0, false,
                Enumerable.Repeat(y, 256).ToArray(),
                Enumerable.Repeat(cb, 64).ToArray(),
                Enumerable.Repeat(cr, 64).ToArray());
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

    private static BitWriter WriteISliceHeader()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(2); // I slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(0, 4); // frame_num
        bits.WriteUe(0); // idr_pic_id
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // no_output_of_prior_pics_flag
        bits.WriteFlag(false); // long_term_reference_flag
        bits.WriteSe(0); // slice_qp_delta
        return bits;
    }

    private static BitWriter WritePSliceHeader(uint sliceType = 0, uint frameNum = 0)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(sliceType);
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(frameNum, 4); // frame_num
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteSe(0); // slice_qp_delta
        return bits;
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteFlag(bool value) => _bits.Add(value);

        public void AlignWithOneBits()
        {
            while ((_bits.Count & 7) != 0)
                WriteFlag(true);
        }

        public void AlignToByte()
        {
            while ((_bits.Count & 7) != 0)
                WriteFlag(false);
        }

        public void WriteBits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                _bits.Add(((value >> i) & 1) != 0);
        }

        public void WriteUe(uint value)
        {
            uint codeNum = checked(value + 1);
            int leadingZeroes = 0;
            for (uint current = codeNum; current > 1; current >>= 1)
                leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++)
                _bits.Add(false);
            WriteBits(codeNum, leadingZeroes + 1);
        }

        public void WriteSe(int value)
        {
            uint codeNum = value <= 0
                ? checked((uint)(-2L * value))
                : checked((uint)(2L * value - 1));
            WriteUe(codeNum);
        }

        public void WriteRbspTrailingBits()
        {
            WriteFlag(true);
            while ((_bits.Count & 7) != 0)
                WriteFlag(false);
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

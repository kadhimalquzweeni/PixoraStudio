using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264BInterSliceDataDecoderTests
{
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
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0);

        Assert.Equal(H264SliceType.B, result.SliceHeader.SliceType);
        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Single(result.InterMacroblocks);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
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
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0);

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
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 0, 0);

        Assert.Equal(2, result.MacroblocksDecoded);
        Assert.Equal(2, result.InterMacroblocks.Count);
        Assert.True(destination.IsMacroblockDecoded(0, 0));
        Assert.True(destination.IsMacroblockDecoded(1, 0));
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
        H264DecodedPictureSurface reference = Reference(sps, 41);
        H264DecodedPictureBuffer dpb = new();
        H264MotionVectorPictureState collocatedL0 = new(1, 1);
        H264MotionVectorPictureState collocatedL1 = new(1, 1);
        collocatedL0.RecordInterPartition(0, 0, 4, 4, 0, 0, new H264MotionVector(2, -2));
        dpb.StoreDecodedPicture(
            reference, new H264ReferencePicture(0, 0), true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, false, false, []),
            motionVectorStateL0: collocatedL0,
            motionVectorStateL1: collocatedL1,
            referenceList0: [new H264ReferencePicture(0, -2)],
            referenceList1: []);

        BitWriter bits = WriteBSliceHeader(directSpatial: false);
        bits.Ue(1);
        bits.TrailingBits();
        H264DecodedPictureSurface destination = new(sps);

        H264BInterSliceDecodeResult result = Decoder(destination).Decode(
            new H264NalUnit(0x41, bits.ToArray()), sps, Pps(), dpb, 2, 0);

        H264BInterMacroblockReconstructionResult skipped =
            Assert.Single(result.InterMacroblocks);
        Assert.True(skipped.IsSkipped);
        H264ReconstructedBInterSubPartition first = skipped.Partitions[0];
        Assert.Equal(new H264MotionVector(-2, 2), first.MotionVectorL0);
        Assert.Equal(new H264MotionVector(-4, 4), first.MotionVectorL1);
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
    }

    [Fact]
    public void Decode_ShouldRejectNonBAndUnsupportedConfiguration()
    {
        H264SequenceParameterSet sps = Sps();
        H264DecodedPictureSurface reference = Reference(sps, 1);
        H264DecodedPictureBuffer dpb = Dpb(reference);
        BitWriter p = WriteBSliceHeader(sliceType: 0);
        p.TrailingBits();
        Assert.Throws<NotSupportedException>(() => Decoder(new H264DecodedPictureSurface(sps)).Decode(
            new H264NalUnit(0x41, p.ToArray()), sps, Pps(), dpb, 0, 0));
        Assert.Throws<NotSupportedException>(() => Decoder(new H264DecodedPictureSurface(sps)).Decode(
            new H264NalUnit(0x41, WriteBSliceHeader().ToArray()), sps,
            Pps() with { NumSliceGroupsMinus1 = 1 }, dpb, 0, 0));
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
            surface.WriteMacroblock(x, 0, 0, false,
                Enumerable.Repeat(value, 256).ToArray(),
                Enumerable.Repeat(value, 64).ToArray(),
                Enumerable.Repeat(value, 64).ToArray());
        return surface;
    }

    private static H264DecodedPictureBuffer Dpb(H264DecodedPictureSurface surface)
    {
        H264DecodedPictureBuffer dpb = new();
        dpb.StoreDecodedPicture(surface, new H264ReferencePicture(0, 0),
            true, 4, 0, 16,
            new H264DecodedReferenceMarking(false, false, false, []));
        return dpb;
    }

    private static BitWriter WriteBSliceHeader(uint sliceType = 1, bool directSpatial = true)
    {
        BitWriter bits = new();
        bits.Ue(0); // first_mb_in_slice
        bits.Ue(sliceType);
        bits.Ue(0); // pic_parameter_set_id
        bits.Bits(1, 4); // frame_num
        bits.Bits(0, 4); // pic_order_cnt_lsb
        bits.Flag(directSpatial); // direct_spatial_mv_pred_flag
        bits.Flag(false); // num_ref_idx_active_override_flag
        bits.Flag(false); // ref_pic_list_modification_flag_l0
        bits.Flag(false); // ref_pic_list_modification_flag_l1
        bits.Flag(false); // adaptive_ref_pic_marking_mode_flag
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

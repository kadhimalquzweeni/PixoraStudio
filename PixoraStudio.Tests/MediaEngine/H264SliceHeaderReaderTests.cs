using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264SliceHeaderReaderTests
{
    [Fact]
    public void Read_ShouldParseIdrISlicePictureOrderAndReferenceMarking()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(2); // I slice
        bits.WriteUe(0); // PPS id
        bits.WriteBits(3, 4); // frame_num
        bits.WriteUe(4); // idr_pic_id
        bits.WriteBits(7, 4); // pic_order_cnt_lsb
        bits.WriteFlag(true); // no_output_of_prior_pics_flag
        bits.WriteFlag(false); // long_term_reference_flag
        bits.WriteSe(-2); // slice_qp_delta
        bits.WriteUe(1); // disable_deblocking_filter_idc

        H264SliceHeader result = new H264SliceHeaderReader().Read(
            new H264NalUnit(0x65, bits.ToArray()), CreateSps(), CreatePps());

        Assert.Equal(H264SliceType.I, result.SliceType);
        Assert.Equal(3u, result.FrameNum);
        Assert.Equal(4u, result.IdrPicId);
        Assert.Equal(7u, result.PicOrderCntLsb);
        Assert.Equal(-2, result.SliceQpDelta);
        Assert.True(result.DecodedReferenceMarking!.NoOutputOfPriorPicsFlag);
        Assert.Equal(1u, result.DisableDeblockingFilterIdc);
        Assert.Empty(result.RefPicList0Modifications);
    }

    [Fact]
    public void Read_ShouldParsePSliceReferenceListModificationAndMmco()
    {
        BitWriter bits = new();
        bits.WriteUe(5);
        bits.WriteUe(0); // P slice
        bits.WriteUe(0);
        bits.WriteBits(2, 4);
        bits.WriteBits(4, 4); // POC
        bits.WriteFlag(false); // ref count override
        bits.WriteFlag(true); // list 0 modification present
        bits.WriteUe(0); // subtract abs diff
        bits.WriteUe(2);
        bits.WriteUe(3); // terminator
        bits.WriteFlag(true); // adaptive ref marking
        bits.WriteUe(1); // MMCO 1
        bits.WriteUe(3); // difference_of_pic_nums_minus1
        bits.WriteUe(0); // terminator
        bits.WriteSe(1); // QP delta
        bits.WriteUe(0); // deblocking enabled
        bits.WriteSe(-1);
        bits.WriteSe(2);

        H264SliceHeader result = new H264SliceHeaderReader().Read(
            new H264NalUnit(0x61, bits.ToArray()), CreateSps(), CreatePps());

        Assert.Equal(H264SliceType.P, result.SliceType);
        Assert.Equal(5u, result.FirstMacroblockInSlice);
        Assert.Equal(new H264ReferenceListModification(0, 2, null), Assert.Single(result.RefPicList0Modifications));
        Assert.True(result.DecodedReferenceMarking!.AdaptiveRefPicMarkingModeFlag);
        H264MemoryManagementControlOperation mmco = Assert.Single(result.DecodedReferenceMarking.Operations);
        Assert.Equal(1u, mmco.MemoryManagementControlOperation);
        Assert.Equal(3u, mmco.DifferenceOfPicNumsMinus1);
        Assert.Equal(-1, result.SliceAlphaC0OffsetDiv2);
        Assert.Equal(2, result.SliceBetaOffsetDiv2);
    }

    [Fact]
    public void Read_ShouldParseBSliceWeightedPrediction()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(1); // B slice
        bits.WriteUe(0);
        bits.WriteBits(1, 4);
        bits.WriteBits(2, 4);
        bits.WriteFlag(true); // direct_spatial_mv_pred_flag
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // list 0 modification
        bits.WriteFlag(false); // list 1 modification
        bits.WriteUe(0); // luma denominator
        bits.WriteUe(0); // chroma denominator
        bits.WriteFlag(true); // L0 luma weight present
        bits.WriteSe(2);
        bits.WriteSe(-1);
        bits.WriteFlag(false); // L0 chroma weight absent
        bits.WriteFlag(false); // L1 luma weight absent
        bits.WriteFlag(false); // L1 chroma weight absent
        bits.WriteFlag(false); // adaptive marking
        bits.WriteSe(0); // QP delta
        bits.WriteUe(1); // deblocking disabled

        H264PictureParameterSetHeader pps = CreatePps() with { WeightedBipredIdc = 1 };
        H264SliceHeader result = new H264SliceHeaderReader().Read(
            new H264NalUnit(0x61, bits.ToArray()), CreateSps(), pps);

        Assert.Equal(H264SliceType.B, result.SliceType);
        Assert.True(result.DirectSpatialMvPredFlag);
        Assert.NotNull(result.PredictionWeights);
        Assert.Equal(0u, result.PredictionWeights.LumaLog2WeightDenom);
        H264PredictionWeight l0 = Assert.Single(result.PredictionWeights.List0);
        Assert.True(l0.LumaWeightFlag);
        Assert.Equal(2, l0.LumaWeight);
        Assert.Equal(-1, l0.LumaOffset);
        Assert.Single(result.PredictionWeights.List1);
    }

    [Fact]
    public void Read_ShouldParsePocTypeOneAndFieldPictureSyntax()
    {
        H264SequenceParameterSet sps = CreateSps() with
        {
            PicOrderCntType = 1,
            FrameMbsOnlyFlag = false,
            DeltaPicOrderAlwaysZeroFlag = false
        };
        H264PictureParameterSetHeader pps = CreatePps() with
        {
            BottomFieldPicOrderInFramePresentFlag = true
        };
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(2); // I slice
        bits.WriteUe(0);
        bits.WriteBits(9, 4); // frame_num
        bits.WriteFlag(true); // field_pic_flag
        bits.WriteFlag(true); // bottom_field_flag
        bits.WriteSe(-2); // delta_pic_order_cnt[0]
        bits.WriteSe(3); // slice_qp_delta
        bits.WriteUe(1); // deblocking disabled

        H264SliceHeader result = new H264SliceHeaderReader().Read(
            new H264NalUnit(0x01, bits.ToArray()), sps, pps);

        Assert.Equal(9u, result.FrameNum);
        Assert.True(result.FieldPicFlag);
        Assert.True(result.BottomFieldFlag);
        Assert.Equal(-2, result.DeltaPicOrderCnt0);
        Assert.Null(result.DeltaPicOrderCnt1);
        Assert.Equal(3, result.SliceQpDelta);
    }

    [Fact]
    public void Read_ShouldParseSpSliceCabacAndSwitchQuantiserSyntax()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(3); // SP slice
        bits.WriteUe(0);
        bits.WriteBits(0, 4);
        bits.WriteBits(0, 4);
        bits.WriteFlag(false); // ref count override
        bits.WriteFlag(false); // list modification
        bits.WriteUe(2); // cabac_init_idc
        bits.WriteSe(-1); // slice_qp_delta
        bits.WriteFlag(true); // sp_for_switch_flag
        bits.WriteSe(-2); // slice_qs_delta
        bits.WriteUe(1); // deblocking disabled

        H264PictureParameterSetHeader pps = CreatePps() with { EntropyCodingModeFlag = true };
        H264SliceHeader result = new H264SliceHeaderReader().Read(
            new H264NalUnit(0x01, bits.ToArray()), CreateSps(), pps);

        Assert.Equal(H264SliceType.Sp, result.SliceType);
        Assert.Equal(2u, result.CabacInitIdc);
        Assert.True(result.SpForSwitchFlag);
        Assert.Equal(-2, result.SliceQsDelta);
    }

    [Fact]
    public void Read_ShouldParseSliceGroupChangeCycle()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(2); // I slice
        bits.WriteUe(0);
        bits.WriteBits(0, 4);
        bits.WriteBits(0, 4);
        bits.WriteSe(0); // QP delta
        bits.WriteUe(1); // deblocking disabled
        bits.WriteBits(257, 9); // ceil(log2(920 / 2 + 1)) bits

        H264PictureParameterSetHeader pps = new(0, 0, false, false, 1)
        {
            SliceGroupMapType = 3,
            SliceGroupChangeDirectionFlag = false,
            SliceGroupChangeRateMinus1 = 1,
            DeblockingFilterControlPresentFlag = true
        };
        H264SliceHeader result = new H264SliceHeaderReader().Read(
            new H264NalUnit(0x01, bits.ToArray()), CreateSps(), pps);

        Assert.Equal(257u, result.SliceGroupChangeCycle);
    }

    [Fact]
    public void Read_ShouldRejectTruncatedSliceHeader()
    {
        Assert.Throws<EndOfStreamException>(() => new H264SliceHeaderReader().Read(
            new H264NalUnit(0x41, [0x80]), CreateSps(), CreatePps()));
    }

    [Fact]
    public void Read_ShouldRejectMismatchedPpsAndUnsupportedNalTypes()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(2);
        bits.WriteUe(1);
        Assert.Throws<FormatException>(() => new H264SliceHeaderReader().Read(
            new H264NalUnit(0x41, bits.ToArray()), CreateSps(), CreatePps()));

        Assert.Throws<FormatException>(() => new H264SliceHeaderReader().Read(
            new H264NalUnit(0x67, [0x80]), CreateSps(), CreatePps()));
    }

    private static H264SequenceParameterSet CreateSps() => new(
        0, 66, 30, 1, false, 0, 0, 0, 0, 0, 2,
        39, 22, true, true, 640, 368, false);

    private static H264PictureParameterSetHeader CreatePps() => new(0, 0, false, false, 0)
    {
        NumRefIdxL0DefaultActiveMinus1 = 0,
        NumRefIdxL1DefaultActiveMinus1 = 0,
        DeblockingFilterControlPresentFlag = true
    };

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteFlag(bool value) => _bits.Add(value);

        public void WriteBits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                _bits.Add(((value >> i) & 1) != 0);
        }

        public void WriteUe(uint value)
        {
            uint codeNum = checked(value + 1);
            int leadingZeroes = 0;
            for (uint current = codeNum; current > 1; current >>= 1) leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++) _bits.Add(false);
            WriteBits(codeNum, leadingZeroes + 1);
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
                if (_bits[i]) bytes[i / 8] |= (byte)(1 << (7 - i % 8));
            return bytes;
        }
    }
}

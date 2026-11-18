using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264ParameterSetReaderTests
{
    [Fact]
    public void ReadSps_ShouldParseBaselineProfileAndCodedDimensions()
    {
        BitWriter bits = new();
        bits.WriteBits(66, 8); // Baseline profile
        bits.WriteBits(0, 8);  // Constraints
        bits.WriteBits(30, 8); // Level 3.0
        bits.WriteUe(0);       // SPS id
        bits.WriteUe(0);       // log2_max_frame_num_minus4
        bits.WriteUe(0);       // pic_order_cnt_type
        bits.WriteUe(0);       // log2_max_pic_order_cnt_lsb_minus4
        bits.WriteUe(1);       // max_num_ref_frames
        bits.WriteFlag(false); // gaps
        bits.WriteUe(39);      // 640 pixels
        bits.WriteUe(22);      // 368 pixels
        bits.WriteFlag(true);  // frame_mbs_only
        bits.WriteFlag(true);  // direct_8x8_inference
        bits.WriteFlag(false); // cropping
        bits.WriteFlag(false); // VUI

        H264NalUnit nal = new(0x67, bits.ToArray());
        var result = new H264SequenceParameterSetReader().Read(nal);

        Assert.Equal((byte)66, result.ProfileIdc);
        Assert.Equal((byte)30, result.LevelIdc);
        Assert.Equal(640u, result.FrameWidth);
        Assert.Equal(368u, result.FrameHeight);
        Assert.True(result.FrameMbsOnlyFlag);
        Assert.False(result.VuiParametersPresentFlag);
    }

    [Fact]
    public void ReadSps_ShouldApplyFrameCropping()
    {
        BitWriter bits = new();
        bits.WriteBits(66, 8);
        bits.WriteBits(0, 8);
        bits.WriteBits(30, 8);
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteUe(1);
        bits.WriteFlag(false);
        bits.WriteUe(39);
        bits.WriteUe(22);
        bits.WriteFlag(true);
        bits.WriteFlag(true);
        bits.WriteFlag(true);
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteUe(2);
        bits.WriteUe(2);
        bits.WriteFlag(false);

        var result = new H264SequenceParameterSetReader().Read(new H264NalUnit(0x67, bits.ToArray()));

        // 4:2:0 crop units are 2x2 luma samples.
        Assert.Equal(640u, result.FrameWidth);
        Assert.Equal(360u, result.FrameHeight);
    }

    [Fact]
    public void ReadSps_ShouldParseVuiAspectRatioTimingAndBitstreamRestrictions()
    {
        BitWriter bits = CreateBaselineSpsPrefix();
        bits.WriteFlag(true); // vui_parameters_present_flag
        bits.WriteBits(255, 8); // extended SAR
        bits.WriteBits(4, 16);
        bits.WriteBits(3, 16);
        bits.WriteFlag(false); // overscan_info_present_flag
        bits.WriteFlag(false); // video_signal_type_present_flag
        bits.WriteFlag(false); // chroma_loc_info_present_flag
        bits.WriteFlag(true);  // timing_info_present_flag
        bits.WriteBits(1001, 32);
        bits.WriteBits(60000, 32);
        bits.WriteFlag(true);  // fixed_frame_rate_flag
        bits.WriteFlag(false); // nal_hrd_parameters_present_flag
        bits.WriteFlag(false); // vcl_hrd_parameters_present_flag
        bits.WriteFlag(false); // pic_struct_present_flag
        bits.WriteFlag(true);  // bitstream_restriction_flag
        bits.WriteFlag(true);  // motion_vectors_over_pic_boundaries_flag
        bits.WriteUe(2);
        bits.WriteUe(1);
        bits.WriteUe(16);
        bits.WriteUe(16);
        bits.WriteUe(2);
        bits.WriteUe(4);

        var result = new H264SequenceParameterSetReader().Read(
            new H264NalUnit(0x67, bits.ToArray()));

        Assert.NotNull(result.VuiParameters);
        Assert.Equal(255u, result.VuiParameters.AspectRatioIdc);
        Assert.Equal(4u, result.VuiParameters.SarWidth);
        Assert.Equal(3u, result.VuiParameters.SarHeight);
        Assert.Equal(1001u, result.VuiParameters.NumUnitsInTick);
        Assert.Equal(60000u, result.VuiParameters.TimeScale);
        Assert.True(result.VuiParameters.FixedFrameRateFlag);
        Assert.Equal(2u, result.VuiParameters.MaxNumReorderFrames);
        Assert.Equal(4u, result.VuiParameters.MaxDecFrameBuffering);
    }

    [Fact]
    public void ReadSps_ShouldParseNalHrdParameters()
    {
        BitWriter bits = CreateBaselineSpsPrefix();
        bits.WriteFlag(true);  // VUI present
        bits.WriteBits(1, 8);   // square pixels
        bits.WriteFlag(false); // overscan
        bits.WriteFlag(false); // video signal
        bits.WriteFlag(false); // chroma location
        bits.WriteFlag(false); // timing
        bits.WriteFlag(true);  // NAL HRD present
        bits.WriteUe(0);        // cpb_cnt_minus1
        bits.WriteBits(0, 4);   // bit_rate_scale
        bits.WriteBits(0, 4);   // cpb_size_scale
        bits.WriteUe(10);
        bits.WriteUe(20);
        bits.WriteFlag(true);
        bits.WriteBits(23, 5);
        bits.WriteBits(23, 5);
        bits.WriteBits(23, 5);
        bits.WriteBits(0, 5);
        bits.WriteFlag(false); // VCL HRD absent
        bits.WriteFlag(true);  // low_delay_hrd_flag
        bits.WriteFlag(false); // pic_struct_present_flag
        bits.WriteFlag(false); // bitstream_restriction_flag

        var result = new H264SequenceParameterSetReader().Read(
            new H264NalUnit(0x67, bits.ToArray()));

        Assert.NotNull(result.VuiParameters?.NalHrd);
        Assert.Equal(1u, result.VuiParameters.NalHrd.CpbCount);
        Assert.Equal(10u, result.VuiParameters.NalHrd.Entries[0].BitRateValueMinus1);
        Assert.Equal(20u, result.VuiParameters.NalHrd.Entries[0].CpbSizeValueMinus1);
        Assert.True(result.VuiParameters.NalHrd.Entries[0].CbrFlag);
        Assert.True(result.VuiParameters.LowDelayHrdFlag);
    }

    private static BitWriter CreateBaselineSpsPrefix()
    {
        BitWriter bits = new();
        bits.WriteBits(66, 8);
        bits.WriteBits(0, 8);
        bits.WriteBits(30, 8);
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteUe(1);
        bits.WriteFlag(false);
        bits.WriteUe(39);
        bits.WriteUe(22);
        bits.WriteFlag(true);
        bits.WriteFlag(true);
        bits.WriteFlag(false);
        return bits;
    }

    [Fact]
    public void ReadSps_ShouldRejectWrongNalType()
    {
        Assert.Throws<FormatException>(() =>
            new H264SequenceParameterSetReader().Read(new H264NalUnit(0x68, [0x80])));
    }

    [Fact]
    public void ReadPps_ShouldParseLeadingHeaderFields()
    {
        BitWriter bits = new();
        bits.WriteUe(3);
        bits.WriteUe(2);
        bits.WriteFlag(true);
        bits.WriteFlag(false);
        bits.WriteUe(0);
        WritePpsTail(bits);

        var result = new H264PictureParameterSetReader().Read(new H264NalUnit(0x68, bits.ToArray(appendTrailingBits: false)));

        Assert.Equal(3u, result.Id);
        Assert.Equal(2u, result.SequenceParameterSetId);
        Assert.True(result.EntropyCodingModeFlag);
        Assert.False(result.BottomFieldPicOrderInFramePresentFlag);
        Assert.Equal(0u, result.NumSliceGroupsMinus1);
        Assert.Equal(1u, result.NumRefIdxL0DefaultActiveMinus1);
        Assert.Equal(1u, result.NumRefIdxL1DefaultActiveMinus1);
        Assert.True(result.DeblockingFilterControlPresentFlag);
        Assert.False(result.HasExtension);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ReadPps_ShouldParseSupportedFlexibleMacroblockOrderingMapTypes(uint mapType)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // PPS id
        bits.WriteUe(0); // SPS id
        bits.WriteFlag(false);
        bits.WriteFlag(false);
        bits.WriteUe(1); // two slice groups
        bits.WriteUe(mapType);
        switch (mapType)
        {
            case 0:
                bits.WriteUe(2);
                bits.WriteUe(3);
                break;
            case 2:
                bits.WriteUe(0);
                bits.WriteUe(5);
                break;
            case 3:
            case 4:
            case 5:
                bits.WriteFlag(true);
                bits.WriteUe(4);
                break;
            case 6:
                bits.WriteUe(3); // four map units
                bits.WriteBits(0, 1);
                bits.WriteBits(1, 1);
                bits.WriteBits(0, 1);
                bits.WriteBits(1, 1);
                break;
        }
        WritePpsTail(bits);

        var result = new H264PictureParameterSetReader().Read(
            new H264NalUnit(0x68, bits.ToArray(appendTrailingBits: false)));

        Assert.Equal(mapType, result.SliceGroupMapType);
        if (mapType == 6)
            Assert.Equal(new uint[] { 0, 1, 0, 1 }, result.SliceGroupId);
        if (mapType == 0)
            Assert.Equal(new uint[] { 2, 3 }, result.RunLengthMinus1);
        if (mapType == 2)
            Assert.Equal(new H264SliceGroupRectangle(0, 5), Assert.Single(result.SliceGroupRectangles));
    }

    [Fact]
    public void ReadPps_ShouldParseHighProfileScalingMatrixExtension()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteFlag(false);
        bits.WriteFlag(false);
        bits.WriteUe(0);
        WritePpsTail(bits, includeTrailingBits: false);
        bits.WriteFlag(true);  // transform_8x8_mode_flag
        bits.WriteFlag(true);  // pic_scaling_matrix_present_flag
        for (int i = 0; i < 8; i++)
        {
            bits.WriteFlag(i == 0);
            if (i == 0)
                for (int j = 0; j < 16; j++) bits.WriteSe(0);
        }
        bits.WriteSe(2); // second_chroma_qp_index_offset
        WriteRbspTrailingBits(bits);

        var result = new H264PictureParameterSetReader().Read(
            new H264NalUnit(0x68, bits.ToArray(appendTrailingBits: false)));

        Assert.True(result.HasExtension);
        Assert.True(result.Transform8X8ModeFlag);
        Assert.True(result.PicScalingMatrixPresentFlag);
        Assert.Equal(8, result.ScalingLists.Count);
        Assert.Equal(16, result.ScalingLists[0].Count);
        Assert.Equal(2, result.SecondChromaQpIndexOffset);
    }

    [Fact]
    public void ReadSps_ShouldRetainScalingListUseDefaultSyntax()
    {
        BitWriter bits = new();
        bits.WriteBits(100, 8); // High profile
        bits.WriteBits(0, 8);
        bits.WriteBits(40, 8);
        bits.WriteUe(0); // SPS id
        bits.WriteUe(1); // 4:2:0
        bits.WriteUe(0); // luma bit depth
        bits.WriteUe(0); // chroma bit depth
        bits.WriteFlag(false); // qpprime_y_zero_transform_bypass_flag
        bits.WriteFlag(true); // seq_scaling_matrix_present_flag
        bits.WriteFlag(true); // first 4x4 list present
        bits.WriteSe(-8); // nextScale becomes zero: use default matrix
        for (int i = 1; i < 8; i++) bits.WriteFlag(false);

        bits.WriteUe(0); // log2_max_frame_num_minus4
        bits.WriteUe(0); // pic_order_cnt_type
        bits.WriteUe(0); // log2_max_pic_order_cnt_lsb_minus4
        bits.WriteUe(1); // max_num_ref_frames
        bits.WriteFlag(false);
        bits.WriteUe(39);
        bits.WriteUe(22);
        bits.WriteFlag(true);
        bits.WriteFlag(true);
        bits.WriteFlag(false);
        bits.WriteFlag(false);

        H264SequenceParameterSet result = new H264SequenceParameterSetReader()
            .Read(new H264NalUnit(0x67, bits.ToArray()));

        Assert.True(result.ScalingMatrixPresentFlag);
        Assert.Equal(8, result.ScalingLists.Count);
        Assert.True(result.ScalingListUseDefault[0]);
        Assert.Empty(result.ScalingLists[1]);
    }

    [Fact]
    public void ReadPps_ShouldRetainScalingListUseDefaultSyntax()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteFlag(false);
        bits.WriteFlag(false);
        bits.WriteUe(0);
        WritePpsTail(bits, includeTrailingBits: false);
        bits.WriteFlag(false); // transform_8x8_mode_flag
        bits.WriteFlag(true); // pic_scaling_matrix_present_flag
        bits.WriteFlag(true); // first 4x4 list present
        bits.WriteSe(-8); // use default matrix
        for (int i = 1; i < 6; i++) bits.WriteFlag(false);
        bits.WriteSe(0); // second_chroma_qp_index_offset
        WriteRbspTrailingBits(bits);

        H264SequenceParameterSet sps = CreateBaselineSpsForPpsExtension();
        H264PictureParameterSetHeader result = new H264PictureParameterSetReader()
            .Read(new H264NalUnit(0x68, bits.ToArray(appendTrailingBits: false)), sps);

        Assert.True(result.PicScalingMatrixPresentFlag);
        Assert.True(result.ScalingListUseDefault[0]);
        Assert.Empty(result.ScalingLists[1]);
    }

    [Fact]
    public void ReadPps_ShouldRejectScalingListDeltaOutsidePermittedRange()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteFlag(false);
        bits.WriteFlag(false);
        bits.WriteUe(0);
        WritePpsTail(bits, includeTrailingBits: false);
        bits.WriteFlag(false);
        bits.WriteFlag(true);
        bits.WriteFlag(true);
        bits.WriteSe(-129);
        WriteRbspTrailingBits(bits);

        Assert.Throws<FormatException>(() =>
            new H264PictureParameterSetReader().Read(
                new H264NalUnit(0x68, bits.ToArray(appendTrailingBits: false)),
                CreateBaselineSpsForPpsExtension()));
    }

    private static H264SequenceParameterSet CreateBaselineSpsForPpsExtension() =>
        new(0, 66, 30, 1, false, 0, 0, 0, 0, 0, 1,
            39, 22, true, true, 640, 368, false);

    [Fact]
    public void ReadSps_ShouldRejectUnknownProfileSyntax()
    {
        BitWriter bits = CreateBaselineSpsPrefix();
        bits.WriteFlag(false); // vui_parameters_present_flag
        byte[] rbsp = bits.ToArray();
        rbsp[0] = 200;

        Assert.Throws<NotSupportedException>(() =>
            new H264SequenceParameterSetReader().Read(new H264NalUnit(0x67, rbsp)));
    }

    [Fact]
    public void ReadSps_ShouldRejectInvalidRbspTrailingBits()
    {
        BitWriter bits = CreateBaselineSpsPrefix();
        bits.WriteFlag(false); // vui_parameters_present_flag
        byte[] rbsp = bits.ToArray();
        rbsp[^1] = 0;

        Assert.Throws<FormatException>(() =>
            new H264SequenceParameterSetReader().Read(new H264NalUnit(0x67, rbsp)));
    }

    [Fact]
    public void ReadPps_ShouldRejectInvalidRbspTrailingBits()
    {
        BitWriter bits = new();
        bits.WriteUe(0);
        bits.WriteUe(0);
        bits.WriteFlag(false);
        bits.WriteFlag(false);
        bits.WriteUe(0);
        WritePpsTail(bits, includeTrailingBits: false);
        byte[] rbsp = bits.ToArray();
        rbsp[^1] = 0;

        Assert.Throws<FormatException>(() =>
            new H264PictureParameterSetReader().Read(new H264NalUnit(0x68, rbsp)));
    }

    [Fact]
    public void ReadPps_ShouldRejectWrongNalType()
    {
        Assert.Throws<FormatException>(() =>
            new H264PictureParameterSetReader().Read(new H264NalUnit(0x67, [0x80])));
    }

    private static void WritePpsTail(BitWriter bits, bool includeTrailingBits = true)
    {
        bits.WriteUe(1); // default L0 references minus one
        bits.WriteUe(1); // default L1 references minus one
        bits.WriteFlag(false);
        bits.WriteBits(0, 2);
        bits.WriteSe(0);
        bits.WriteSe(0);
        bits.WriteSe(0);
        bits.WriteFlag(true);
        bits.WriteFlag(false);
        bits.WriteFlag(false);
        if (includeTrailingBits) WriteRbspTrailingBits(bits);
    }

    private static void WriteRbspTrailingBits(BitWriter bits)
    {
        bits.WriteFlag(true);
        while (bits.BitCount % 8 != 0) bits.WriteFlag(false);
    }

    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public int BitCount => _bits.Count;

        public void WriteFlag(bool value) => _bits.Add(value);

        public void WriteBits(uint value, int count)
        {
            for (int i = count - 1; i >= 0; i--)
                _bits.Add(((value >> i) & 1) != 0);
        }

        public void WriteSe(int value)
        {
            uint codeNum = value <= 0 ? checked((uint)(-2L * value)) : checked((uint)(2L * value - 1));
            WriteUe(codeNum);
        }

        public void WriteUe(uint value)
        {
            uint codeNum = value + 1;
            int leadingZeroes = 0;
            for (uint v = codeNum; v > 1; v >>= 1) leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++) _bits.Add(false);
            WriteBits(codeNum, leadingZeroes + 1);
        }

        public byte[] ToArray(bool appendTrailingBits = true)
        {
            List<bool> outputBits = [.. _bits];
            if (appendTrailingBits)
            {
                outputBits.Add(true); // rbsp_stop_one_bit
                while (outputBits.Count % 8 != 0) outputBits.Add(false);
            }

            byte[] result = new byte[(outputBits.Count + 7) / 8];
            for (int i = 0; i < outputBits.Count; i++)
                if (outputBits[i]) result[i / 8] |= (byte)(1 << (7 - (i % 8)));
            return result;
        }
    }
}

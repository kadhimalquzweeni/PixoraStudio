using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Decoders.Video.H264;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264AvcParameterSetCollectionReaderTests
{
    [Fact]
    public void Read_ShouldParseParameterSetsAndResolvePpsSpsRelationship()
    {
        byte[] sps = CreateSps(0);
        byte[] pps = CreatePps(0, 0);
        H264AvcParameterSetCollection result =
            new H264AvcParameterSetCollectionReader().Read(
                CreateConfiguration(sps, pps));

        H264SequenceParameterSet parsedSps = Assert.Single(result.SequenceParameterSets).Value;
        H264PictureParameterSetHeader parsedPps = Assert.Single(result.PictureParameterSets).Value;
        Assert.Equal(0u, parsedSps.Id);
        Assert.Equal(16u, parsedSps.FrameWidth);
        Assert.Equal(16u, parsedSps.FrameHeight);
        Assert.Equal(0u, parsedPps.Id);
        Assert.Equal(parsedSps.Id, parsedPps.SequenceParameterSetId);
    }

    [Fact]
    public void Read_ShouldRejectPpsThatReferencesMissingSps()
    {
        byte[] sps = CreateSps(0);
        byte[] pps = CreatePps(0, 1);

        Assert.Throws<FormatException>(() =>
            new H264AvcParameterSetCollectionReader().Read(
                CreateConfiguration(sps, pps)));
    }

    [Fact]
    public void Read_ShouldRejectDuplicateParameterSetIdentifiers()
    {
        byte[] sps = CreateSps(0);
        byte[] pps = CreatePps(0, 0);
        byte[] duplicateSpsConfiguration = CreateConfiguration(sps, pps, spsCount: 2);

        Assert.Throws<FormatException>(() =>
            new H264AvcParameterSetCollectionReader().Read(duplicateSpsConfiguration));
    }

    private static byte[] CreateConfiguration(
        byte[] sps,
        byte[] pps,
        int spsCount = 1)
    {
        List<byte> data = [1, 66, 0, 30, 0xFF, (byte)(0xE0 | spsCount)];
        for (int i = 0; i < spsCount; i++)
            AddNal(data, sps);
        data.Add(1);
        AddNal(data, pps);
        return data.ToArray();
    }

    private static void AddNal(List<byte> data, byte[] nal)
    {
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)nal.Length));
        data.Add(length[0]);
        data.Add(length[1]);
        data.AddRange(nal);
    }

    private static byte[] CreateSps(uint id)
    {
        BitWriter bits = new();
        bits.WriteBits(66, 8);
        bits.WriteBits(0, 8);
        bits.WriteBits(30, 8);
        bits.WriteUe(id);
        bits.WriteUe(0); // log2_max_frame_num_minus4
        bits.WriteUe(0); // pic_order_cnt_type
        bits.WriteUe(0); // log2_max_pic_order_cnt_lsb_minus4
        bits.WriteUe(1); // max_num_ref_frames
        bits.WriteFlag(false); // gaps_in_frame_num_value_allowed_flag
        bits.WriteUe(0); // pic_width_in_mbs_minus1
        bits.WriteUe(0); // pic_height_in_map_units_minus1
        bits.WriteFlag(true); // frame_mbs_only_flag
        bits.WriteFlag(true); // direct_8x8_inference_flag
        bits.WriteFlag(false); // frame_cropping_flag
        bits.WriteFlag(false); // vui_parameters_present_flag
        return bits.ToArray();
    }

    private static byte[] CreatePps(uint ppsId, uint spsId)
    {
        BitWriter bits = new();
        bits.WriteUe(ppsId);
        bits.WriteUe(spsId);
        bits.WriteFlag(false); // entropy_coding_mode_flag
        bits.WriteFlag(false); // bottom_field_pic_order_in_frame_present_flag
        bits.WriteUe(0); // num_slice_groups_minus1
        bits.WriteUe(0); // num_ref_idx_l0_default_active_minus1
        bits.WriteUe(0); // num_ref_idx_l1_default_active_minus1
        bits.WriteFlag(false); // weighted_pred_flag
        bits.WriteBits(0, 2); // weighted_bipred_idc
        bits.WriteSe(0); // pic_init_qp_minus26
        bits.WriteSe(0); // pic_init_qs_minus26
        bits.WriteSe(0); // chroma_qp_index_offset
        bits.WriteFlag(true); // deblocking_filter_control_present_flag
        bits.WriteFlag(false); // constrained_intra_pred_flag
        bits.WriteFlag(false); // redundant_pic_cnt_present_flag
        return bits.ToArray();
    }

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
            uint code = value + 1;
            int leadingZeroes = 0;
            for (uint current = code; current > 1; current >>= 1)
                leadingZeroes++;
            for (int i = 0; i < leadingZeroes; i++)
                _bits.Add(false);
            WriteBits(code, leadingZeroes + 1);
        }

        public void WriteSe(int value) => WriteUe(value <= 0
            ? checked((uint)(-2L * value))
            : checked((uint)(2L * value - 1)));

        public byte[] ToArray()
        {
            List<bool> bits = [.. _bits, true];
            while (bits.Count % 8 != 0)
                bits.Add(false);
            byte[] result = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i])
                    result[i / 8] |= (byte)(1 << (7 - i % 8));
            return result;
        }
    }
}

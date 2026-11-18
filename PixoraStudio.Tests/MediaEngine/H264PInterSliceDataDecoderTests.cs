using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264PInterSliceDataDecoderTests
{
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
    public void Decode_ShouldRejectIntraMacroblocksInsidePAndWrongReferenceListSize()
    {
        H264SequenceParameterSet sps = Sps(1);
        H264DecodedPictureSurface reference = Reference(sps, 1, 2, 3);
        BitWriter intra = WritePSliceHeader();
        intra.WriteUe(0);
        intra.WriteUe(5); // P-slice I_NxN macroblock type
        intra.WriteRbspTrailingBits();
        Assert.Throws<NotSupportedException>(() =>
            new H264PInterSliceDataDecoder(new H264DecodedPictureSurface(sps),
                new H264MotionVectorPictureState(1, 1)).Decode(
                new H264NalUnit(0x41, intra.ToArray()), sps, Pps(), [reference], 0));

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
            surface.WriteMacroblock(x, 0, 0, false,
                Enumerable.Repeat(y, 256).ToArray(),
                Enumerable.Repeat(cb, 64).ToArray(),
                Enumerable.Repeat(cr, 64).ToArray());
        return surface;
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

    private static BitWriter WritePSliceHeader(uint sliceType = 0)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(sliceType);
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(0, 4); // frame_num
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

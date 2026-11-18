using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264IntraSliceDataDecoderTests
{
    [Theory]
    [InlineData(1, 64)]
    [InlineData(2, 128)]
    public void Decode_IdrIpcmSlice_ShouldReconstructPictureAndConsumeTrailingBits(
        uint chromaFormat, int chromaSamples)
    {
        H264SequenceParameterSet sps = CreateSps(1, 1, chromaFormat);
        H264DecodedPictureSurface surface = new(sps);
        H264IntraSliceDataDecoder decoder = new(surface);
        BitWriter bits = WriteIdrISliceHeader();
        bits.WriteUe(25); // I_PCM macroblock
        bits.AlignWithZeroBits();
        bits.WriteRepeatedByte(0x5A, 256);
        bits.WriteRepeatedByte(0x6B, chromaSamples);
        bits.WriteRepeatedByte(0x7C, chromaSamples);
        bits.WriteRbspTrailingBits();

        H264IntraSliceDecodeResult result = decoder.Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps, CreatePps(), 3);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Equal(26, result.FinalQpY);
        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)0x5A, value));
        Assert.All(surface.CopyCbPlane(), value => Assert.Equal((byte)0x6B, value));
        Assert.All(surface.CopyCrPlane(), value => Assert.Equal((byte)0x7C, value));
        Assert.True(result.BitsConsumed > 0);
    }

    [Fact]
    public void Decode_Intra4x4Slice_ShouldParseSyntaxResidualAndReconstructMacroblock()
    {
        H264SequenceParameterSet sps = CreateSps(1, 1);
        H264DecodedPictureSurface surface = new(sps);
        BitWriter bits = WriteIdrISliceHeader();
        bits.WriteUe(0); // I_NxN
        for (int i = 0; i < 16; i++)
            bits.WriteFlag(true); // prev_intra4x4_pred_mode_flag
        bits.WriteUe(0); // intra_chroma_pred_mode
        bits.WriteUe(3); // coded_block_pattern maps to luma=0/chroma=0
        bits.WriteRbspTrailingBits();

        H264IntraSliceDecodeResult result = new H264IntraSliceDataDecoder(surface).Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps, CreatePps(), 1);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCbPlane(), value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCrPlane(), value => Assert.Equal((byte)128, value));
    }

    [Fact]
    public void Decode_TwoIntra4x4Macroblocks_ShouldTraverseRasterOrderAndShareNeighbourState()
    {
        H264SequenceParameterSet sps = CreateSps(2, 1);
        H264DecodedPictureSurface surface = new(sps);
        BitWriter bits = WriteIdrISliceHeader();
        for (int macroblock = 0; macroblock < 2; macroblock++)
        {
            bits.WriteUe(0); // I_NxN
            for (int i = 0; i < 16; i++)
                bits.WriteFlag(true);
            bits.WriteUe(0); // intra_chroma_pred_mode
            bits.WriteUe(3); // coded_block_pattern maps to zero
        }
        bits.WriteRbspTrailingBits();

        H264IntraSliceDecodeResult result = new H264IntraSliceDataDecoder(surface).Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps, CreatePps(), 7);

        Assert.Equal(2, result.MacroblocksDecoded);
        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.True(surface.IsMacroblockDecoded(1, 0));
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)128, value));
    }

    [Fact]
    public void Decode_Intra16x16Slice_ShouldParseDcResidualAndReconstructMacroblock()
    {
        H264SequenceParameterSet sps = CreateSps(1, 1);
        H264DecodedPictureSurface surface = new(sps);
        BitWriter bits = WriteIdrISliceHeader();
        bits.WriteUe(3); // I_16x16, DC luma/chroma patterns, DC luma prediction
        bits.WriteUe(0); // intra_chroma_pred_mode
        bits.WriteSe(5); // mb_qp_delta; QPY wraps from 50 to 3
        bits.WriteFlag(true); // zero coeff_token for Intra16x16 luma DC
        bits.WriteRbspTrailingBits();

        H264IntraSliceDecodeResult result = new H264IntraSliceDataDecoder(surface).Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps,
            CreatePps() with { PicInitQpMinus26 = 24 }, 2);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.Equal(3, result.FinalQpY);
        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCbPlane(), value => Assert.Equal((byte)128, value));
        Assert.All(surface.CopyCrPlane(), value => Assert.Equal((byte)128, value));
    }

    [Fact]
    public void Decode_Intra8x8Slice_ShouldParseFourModeEntriesAndReconstructMacroblock()
    {
        H264SequenceParameterSet sps = CreateSps(1, 1);
        H264DecodedPictureSurface surface = new(sps);
        BitWriter bits = WriteIdrISliceHeader();
        bits.WriteUe(0); // I_NxN
        bits.WriteFlag(true); // transform_size_8x8_flag
        for (int i = 0; i < 4; i++)
            bits.WriteFlag(true); // prev_intra8x8_pred_mode_flag
        bits.WriteUe(0); // intra_chroma_pred_mode
        bits.WriteUe(3); // coded_block_pattern maps to luma=0/chroma=0
        bits.WriteRbspTrailingBits();

        H264IntraSliceDecodeResult result = new H264IntraSliceDataDecoder(surface).Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps,
            CreatePps() with { Transform8X8ModeFlag = true }, 5);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.True(surface.IsMacroblockDecoded(0, 0));
        Assert.All(surface.CopyLumaPlane(), value => Assert.Equal((byte)128, value));
    }

    [Fact]
    public void Decode_ShouldContinueFromFirstMacroblockInSlice()
    {
        H264SequenceParameterSet sps = CreateSps(2, 1);
        H264DecodedPictureSurface surface = new(sps);
        BitWriter bits = WriteIdrISliceHeader(firstMacroblock: 1);
        bits.WriteUe(25);
        bits.AlignWithZeroBits();
        bits.WriteRepeatedByte(0x22, 256);
        bits.WriteRepeatedByte(0x33, 64);
        bits.WriteRepeatedByte(0x44, 64);
        bits.WriteRbspTrailingBits();

        H264IntraSliceDecodeResult result = new H264IntraSliceDataDecoder(surface).Decode(
            new H264NalUnit(0x65, bits.ToArray()), sps, CreatePps(), 4);

        Assert.Equal(1, result.MacroblocksDecoded);
        Assert.False(surface.IsMacroblockDecoded(0, 0));
        Assert.True(surface.IsMacroblockDecoded(1, 0));
        Assert.Equal((byte)0x22, surface.GetLumaSample(16, 0));
        Assert.Equal((byte)0x33, surface.GetChromaSample(1, 8, 0));
    }

    [Fact]
    public void Decode_ShouldRejectUnsupportedSliceAndFmo()
    {
        H264SequenceParameterSet sps = CreateSps(1, 1);
        BitWriter pSlice = WriteSliceHeader(sliceType: 0, idr: false);
        pSlice.WriteRbspTrailingBits();
        Assert.Throws<NotSupportedException>(() =>
            new H264IntraSliceDataDecoder(new H264DecodedPictureSurface(sps)).Decode(
                new H264NalUnit(0x61, pSlice.ToArray()), sps, CreatePps(), 0));

        BitWriter idr = WriteIdrISliceHeader();
        idr.WriteUe(25);
        idr.AlignWithZeroBits();
        idr.WriteRepeatedByte(0, 256);
        idr.WriteRepeatedByte(0x55, 64);
        idr.WriteRepeatedByte(0x66, 64);
        idr.WriteRbspTrailingBits();
        Assert.Throws<NotSupportedException>(() =>
            new H264IntraSliceDataDecoder(new H264DecodedPictureSurface(sps)).Decode(
                new H264NalUnit(0x65, idr.ToArray()), sps,
                CreatePps() with { NumSliceGroupsMinus1 = 1 }, 0));
    }

    [Fact]
    public void Decode_ShouldRejectMissingMacroblockAndMalformedTrailingBits()
    {
        H264SequenceParameterSet sps = CreateSps(1, 1);
        BitWriter empty = WriteIdrISliceHeader();
        empty.WriteRbspTrailingBits();
        Assert.Throws<FormatException>(() =>
            new H264IntraSliceDataDecoder(new H264DecodedPictureSurface(sps)).Decode(
                new H264NalUnit(0x65, empty.ToArray()), sps, CreatePps(), 0));

        BitWriter malformed = WriteIdrISliceHeader();
        malformed.WriteUe(25);
        malformed.AlignWithZeroBits();
        malformed.WriteRepeatedByte(0x5A, 256);
        malformed.WriteRepeatedByte(0x6B, 64);
        malformed.WriteRepeatedByte(0x7C, 64);
        malformed.WriteFlag(false); // invalid stop bit
        malformed.AlignWithZeroBits();
        Assert.Throws<EndOfStreamException>(() =>
            new H264IntraSliceDataDecoder(new H264DecodedPictureSurface(sps)).Decode(
                new H264NalUnit(0x65, malformed.ToArray()), sps, CreatePps(), 0));
    }

    [Fact]
    public void ResetPictureContexts_ShouldAllowNewPictureState()
    {
        H264SequenceParameterSet sps = CreateSps(1, 1);
        H264IntraSliceDataDecoder decoder = new(new H264DecodedPictureSurface(sps));
        decoder.ResetPictureContexts();
        decoder.ResetPictureContexts();
    }

    private static H264SequenceParameterSet CreateSps(
        int widthInMacroblocks, int heightInMacroblocks, uint chromaFormat = 1) =>
        new(0, 66, 30, chromaFormat, false, 0, 0, 0, 0, 0, 1,
            (uint)(widthInMacroblocks - 1), (uint)(heightInMacroblocks - 1),
            true, true, (uint)(widthInMacroblocks * 16),
            (uint)(heightInMacroblocks * 16), false);

    private static H264PictureParameterSetHeader CreatePps() =>
        new(0, 0, false, false, 0);

    private static BitWriter WriteIdrISliceHeader(uint firstMacroblock = 0) =>
        WriteSliceHeader(firstMacroblock, sliceType: 2, idr: true);

    private static BitWriter WriteSliceHeader(
        uint firstMacroblock = 0, uint sliceType = 2, bool idr = true)
    {
        BitWriter bits = new();
        bits.WriteUe(firstMacroblock);
        bits.WriteUe(sliceType);
        bits.WriteUe(0); // PPS id
        bits.WriteBits(0, 4); // frame_num
        if (idr)
            bits.WriteUe(0); // idr_pic_id
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        if (idr)
        {
            bits.WriteFlag(false); // no_output_of_prior_pics_flag
            bits.WriteFlag(false); // long_term_reference_flag
        }
        else
        {
            if (sliceType is 0 or 1 or 3)
            {
                bits.WriteFlag(false); // num_ref_idx_active_override_flag
                bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
            }
            bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        }
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

        public void AlignWithZeroBits()
        {
            while ((_bits.Count & 7) != 0)
                _bits.Add(false);
        }

        public void WriteRepeatedByte(byte value, int count)
        {
            AlignWithZeroBits();
            for (int i = 0; i < count; i++)
                WriteBits(value, 8);
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

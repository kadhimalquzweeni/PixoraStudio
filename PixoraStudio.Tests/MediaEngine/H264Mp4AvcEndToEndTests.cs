using System.Buffers.Binary;
using PixoraStudio.MediaEngine.Decoders.Video.H264;
using PixoraStudio.MediaEngine.Models;
using PixoraStudio.MediaEngine.Models.General;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.Tests.MediaEngine;

public sealed class H264Mp4AvcEndToEndTests
{
    [Fact]
    public void ReadMp4Sample_ThenDecodeIdrPcmSlice_ShouldProduceExpectedPicture()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp());
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        byte[] sliceNal = BuildNal(0x65, CreateIdrPcmSliceRbsp());

        List<byte> avcC =
        [
            1, 66, 0, 30, 0xFD, 0xE1
        ];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);

        Mp4CodecConfiguration codecConfiguration = new(
            Mp4VideoCodec.Avc, "avcC", avcC.ToArray(),
            1, 66, 0, 30, 2);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc, codecConfiguration);
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        List<byte> sampleData = [];
        AddLengthPrefixedNal(sampleData, sliceNal);
        Mp4EncodedSample encodedSample = new(
            new Mp4SampleLocation(1, 1, 0, (uint)sampleData.Count, 0, 40, true)
            {
                SampleDescriptionIndex = 1
            },
            sampleData.ToArray());

        H264Mp4VideoSample mp4Sample =
            new H264Mp4TrackSampleReader().Read(track, encodedSample);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(mp4Sample.Configuration);
        H264SequenceParameterSet sps = Assert.Single(parameterSets.SequenceParameterSets).Value;
        Assert.Equal(0u, Assert.Single(parameterSets.PictureParameterSets).Value.Id);

        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);
        H264Mp4AccessUnitDecodeResult decodeResult = decoder.Decode(mp4Sample)!;
        H264DecodedPictureSurface surface = decodeResult.Surface;

        Assert.Equal(H264SliceType.I, decodeResult.SliceHeader.SliceType);
        Assert.Equal(0, decodeResult.PictureOrderCount.PictureOrderCount);
        Assert.True(surface.IsComplete);
        Assert.True(surface.HasCompleteDeblockingMetadata);
        Assert.Equal(16u, sps.FrameWidth);
        Assert.Equal(16u, sps.FrameHeight);
        Assert.All(surface.CopyLumaPlane(), sample => Assert.Equal((byte)80, sample));
        Assert.All(surface.CopyCbPlane(), sample => Assert.Equal((byte)96, sample));
        Assert.All(surface.CopyCrPlane(), sample => Assert.Equal((byte)160, sample));
        H264Mp4DecodedVideoFrame output = Assert.Single(decodeResult.ReadyFrames);
        Assert.Equal(9u, output.TrackId);
        Assert.Equal(1u, output.SampleNumber);
        Assert.Equal(0L, output.PresentationTimeTicks);
        Assert.Equal(1000u, output.Timescale);
        Assert.Equal(0.0, mp4Sample.DecodeTimeSeconds, 6);
        Assert.Equal(0.04, mp4Sample.DurationSeconds, 6);
        Assert.True(mp4Sample.IsSyncSample);
        Assert.Single(decoder.DecodedPictureBuffer.Entries);
        Assert.Empty(decoder.Flush());
    }

    [Fact]
    public void ReadMp4Samples_ShouldDecodeBDirectPictureBetweenPastAndFutureReferences()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp(maxNumRefFrames: 2));
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);

        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        H264Mp4VideoSample idrSample = ReadSample(
            track, 1, 0, true,
            BuildNal(0x65, CreateIdrPcmSliceRbsp(luma: 80, cb: 96, cr: 160)));
        H264Mp4VideoSample futurePSample = ReadSample(
            track, 2, 40, false,
            BuildNal(0x61, CreatePcmPSliceRbsp(luma: 120, cb: 140, cr: 200)),
            presentationTimeTicks: 80);
        H264Mp4VideoSample bSample = ReadSample(
            track, 3, 80, false,
            BuildNal(0x01, CreateBDirectSliceRbsp()),
            presentationTimeTicks: 40);

        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(idrSample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 2);

        H264Mp4AccessUnitDecodeResult idrResult = decoder.Decode(idrSample)!;
        Assert.Empty(idrResult.ReadyFrames);
        H264Mp4AccessUnitDecodeResult pResult = decoder.Decode(futurePSample)!;
        Assert.Empty(pResult.ReadyFrames);
        H264Mp4AccessUnitDecodeResult bResult = decoder.Decode(bSample)!;
        Assert.All(pResult.Surface.CopyLumaPlane(), sample => Assert.Equal((byte)120, sample));
        Assert.All(pResult.Surface.CopyCbPlane(), sample => Assert.Equal((byte)140, sample));
        Assert.All(pResult.Surface.CopyCrPlane(), sample => Assert.Equal((byte)200, sample));

        H264Mp4DecodedVideoFrame firstOutput = Assert.Single(bResult.ReadyFrames);
        Assert.Equal(1u, firstOutput.SampleNumber);
        Assert.Equal(0, firstOutput.PictureOrderCount);
        Assert.Equal(0L, firstOutput.PresentationTimeTicks);
        Assert.Equal(H264SliceType.B, bResult.SliceHeader.SliceType);
        Assert.All(bResult.Surface.CopyLumaPlane(), sample => Assert.Equal((byte)100, sample));
        Assert.All(bResult.Surface.CopyCbPlane(), sample => Assert.Equal((byte)118, sample));
        Assert.All(bResult.Surface.CopyCrPlane(), sample => Assert.Equal((byte)180, sample));

        IReadOnlyList<H264Mp4DecodedVideoFrame> remaining = decoder.Flush();
        Assert.Equal(new[] { 3u, 2u }, remaining.Select(frame => frame.SampleNumber));
        Assert.Equal(new[] { 2, 4 }, remaining.Select(frame => frame.PictureOrderCount));
        Assert.Equal(new long[] { 40, 80 }, remaining.Select(frame => frame.PresentationTimeTicks));
        Assert.Equal(2, decoder.DecodedPictureBuffer.Count);
    }

    [Fact]
    public void ReadMp4Samples_ShouldDecodePReferencePictureUsingPreviousIdrSurface()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp());
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);

        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 16, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 16, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        byte[] idrNal = BuildNal(0x65, CreateIdrPcmSliceRbsp());
        byte[] pNal = BuildNal(0x61, CreatePSkipSliceRbsp());
        H264Mp4VideoSample idrSample = ReadSample(
            track, 1, 0, true, idrNal);
        H264Mp4VideoSample pSample = ReadSample(
            track, 2, 40, false, pNal);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(idrSample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);

        H264Mp4AccessUnitDecodeResult idrResult = decoder.Decode(idrSample)!;
        H264Mp4DecodedVideoFrame idrOutput = Assert.Single(idrResult.ReadyFrames);
        Assert.Equal(0, idrOutput.PictureOrderCount);

        H264Mp4AccessUnitDecodeResult pResult = decoder.Decode(pSample)!;
        H264Mp4DecodedVideoFrame pOutput = Assert.Single(pResult.ReadyFrames);

        Assert.Equal(H264SliceType.P, pResult.SliceHeader.SliceType);
        Assert.Equal(2, pOutput.PictureOrderCount);
        Assert.Equal(2u, pOutput.SampleNumber);
        Assert.Equal(idrResult.Surface.CopyLumaPlane(), pResult.Surface.CopyLumaPlane());
        Assert.Equal(idrResult.Surface.CopyCbPlane(), pResult.Surface.CopyCbPlane());
        Assert.Equal(idrResult.Surface.CopyCrPlane(), pResult.Surface.CopyCrPlane());
        Assert.Single(decoder.DecodedPictureBuffer.Entries);
        Assert.Equal(1, decoder.DecodedPictureBuffer.Entries[0].Picture.FrameNum);
    }

    [Fact]
    public void ReadMp4Sample_WithTwoSlices_ShouldAssembleOneCompletePicture()
    {
        byte[] spsNal = BuildNal(0x67, CreateSpsRbsp(picWidthInMbsMinus1: 1));
        byte[] ppsNal = BuildNal(0x68, CreatePpsRbsp());
        byte[] firstSlice = BuildNal(0x65,
            CreateIdrPcmSliceRbsp(firstMacroblock: 0, luma: 80, cb: 96, cr: 160));
        byte[] secondSlice = BuildNal(0x65,
            CreateIdrPcmSliceRbsp(firstMacroblock: 1, luma: 120, cb: 140, cr: 200));

        List<byte> avcC = [1, 66, 0, 30, 0xFD, 0xE1];
        AddLengthPrefixedNal(avcC, spsNal);
        avcC.Add(1);
        AddLengthPrefixedNal(avcC, ppsNal);
        Mp4VideoSampleEntry videoEntry = new(
            "avc1", 1, 32, 16, 72, 72, 1, "", 24,
            Mp4VideoCodec.Avc,
            new Mp4CodecConfiguration(Mp4VideoCodec.Avc, "avcC",
                avcC.ToArray(), 1, 66, 0, 30, 2));
        Mp4Track track = new(
            9, 32, 16, 1000, 1000, Mp4HandlerType.Video,
            [new Mp4SampleDescription(1, "avc1", video: videoEntry)]);

        List<byte> sampleData = [];
        AddLengthPrefixedNal(sampleData, firstSlice);
        AddLengthPrefixedNal(sampleData, secondSlice);
        Mp4EncodedSample encodedSample = new(
            new Mp4SampleLocation(1, 1, 0, (uint)sampleData.Count, 0, 40, true)
            {
                SampleDescriptionIndex = 1
            },
            sampleData.ToArray());

        H264Mp4VideoSample mp4Sample =
            new H264Mp4TrackSampleReader().Read(track, encodedSample);
        H264AvcParameterSetCollection parameterSets =
            new H264AvcParameterSetCollectionReader().Read(mp4Sample.Configuration);
        H264Mp4AccessUnitDecoder decoder = new(
            parameterSets, maxNumReorderFramesFallback: 0);

        H264Mp4AccessUnitDecodeResult result = decoder.Decode(mp4Sample)!;

        Assert.Equal(2, result.Surface.MacroblockWidth);
        Assert.True(result.Surface.IsComplete);
        Assert.All(result.Surface.CopyLumaPlane().Select((value, index) =>
            (value, x: index % 32)), sample =>
        {
            Assert.Equal(sample.x < 16 ? (byte)80 : (byte)120, sample.value);
        });
        Assert.All(result.Surface.CopyCbPlane().Select((value, index) =>
            (value, x: index % 16)), sample =>
        {
            Assert.Equal(sample.x < 8 ? (byte)96 : (byte)140, sample.value);
        });
        Assert.All(result.Surface.CopyCrPlane().Select((value, index) =>
            (value, x: index % 16)), sample =>
        {
            Assert.Equal(sample.x < 8 ? (byte)160 : (byte)200, sample.value);
        });
        Assert.Single(result.ReadyFrames);
    }

    private static H264Mp4VideoSample ReadSample(
        Mp4Track track,
        uint sampleNumber,
        ulong decodeTimeTicks,
        bool isSync,
        byte[] nal,
        long? presentationTimeTicks = null)
    {
        List<byte> sampleData = [];
        AddLengthPrefixedNal(sampleData, nal);
        Mp4EncodedSample encoded = new(
            new Mp4SampleLocation(
                sampleNumber, 1, 0, (uint)sampleData.Count,
                decodeTimeTicks, 40, isSync)
            {
                SampleDescriptionIndex = 1,
                CompositionOffsetTicks = checked((int)(
                    (presentationTimeTicks ?? checked((long)decodeTimeTicks)) -
                    checked((long)decodeTimeTicks)))
            },
            sampleData.ToArray());
        return new H264Mp4TrackSampleReader().Read(track, encoded);
    }

    private static byte[] CreatePcmPSliceRbsp(
        uint luma, uint cb, uint cr)
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(0); // P slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(1, 4); // frame_num
        bits.WriteBits(4, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(0); // mb_skip_run
        bits.WriteUe(30); // P-slice I_PCM macroblock
        bits.AlignWithZeroes();
        for (int i = 0; i < 256; i++) bits.WriteBits(luma, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cb, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cr, 8);
        return bits.ToRbsp();
    }

    private static byte[] CreateBDirectSliceRbsp()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(1); // B slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(2, 4); // frame_num
        bits.WriteBits(2, 4); // pic_order_cnt_lsb
        bits.WriteFlag(true); // direct_spatial_mv_pred_flag
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l1
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(0); // mb_skip_run
        bits.WriteUe(0); // B_Direct_16x16
        bits.WriteUe(0); // coded_block_pattern (inter)
        return bits.ToRbsp();
    }

    private static byte[] CreatePSkipSliceRbsp()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // first_mb_in_slice
        bits.WriteUe(0); // P slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(1, 4); // frame_num
        bits.WriteBits(2, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // num_ref_idx_active_override_flag
        bits.WriteFlag(false); // ref_pic_list_modification_flag_l0
        bits.WriteFlag(false); // adaptive_ref_pic_marking_mode_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(1); // mb_skip_run: one P_Skip macroblock
        return bits.ToRbsp();
    }

    private static byte[] CreateSpsRbsp(uint picWidthInMbsMinus1 = 0, uint maxNumRefFrames = 1)
    {
        BitWriter bits = new();
        bits.WriteBits(66, 8);
        bits.WriteBits(0, 8);
        bits.WriteBits(30, 8);
        bits.WriteUe(0); // seq_parameter_set_id
        bits.WriteUe(0); // log2_max_frame_num_minus4
        bits.WriteUe(0); // pic_order_cnt_type
        bits.WriteUe(0); // log2_max_pic_order_cnt_lsb_minus4
        bits.WriteUe(maxNumRefFrames); // max_num_ref_frames
        bits.WriteFlag(false); // gaps_in_frame_num_value_allowed_flag
        bits.WriteUe(picWidthInMbsMinus1); // pic_width_in_mbs_minus1
        bits.WriteUe(0); // pic_height_in_map_units_minus1
        bits.WriteFlag(true); // frame_mbs_only_flag
        bits.WriteFlag(true); // direct_8x8_inference_flag
        bits.WriteFlag(false); // frame_cropping_flag
        bits.WriteFlag(false); // vui_parameters_present_flag
        return bits.ToRbsp();
    }

    private static byte[] CreatePpsRbsp()
    {
        BitWriter bits = new();
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteUe(0); // seq_parameter_set_id
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
        return bits.ToRbsp();
    }

    private static byte[] CreateIdrPcmSliceRbsp(
        uint firstMacroblock = 0, uint luma = 80, uint cb = 96, uint cr = 160)
    {
        BitWriter bits = new();
        bits.WriteUe(firstMacroblock); // first_mb_in_slice
        bits.WriteUe(2); // I slice
        bits.WriteUe(0); // pic_parameter_set_id
        bits.WriteBits(0, 4); // frame_num
        bits.WriteUe(0); // idr_pic_id
        bits.WriteBits(0, 4); // pic_order_cnt_lsb
        bits.WriteFlag(false); // no_output_of_prior_pics_flag
        bits.WriteFlag(false); // long_term_reference_flag
        bits.WriteSe(0); // slice_qp_delta
        bits.WriteUe(0); // disable_deblocking_filter_idc
        bits.WriteSe(0); // slice_alpha_c0_offset_div2
        bits.WriteSe(0); // slice_beta_offset_div2
        bits.WriteUe(25); // I_PCM macroblock
        bits.AlignWithZeroes();

        for (int i = 0; i < 256; i++) bits.WriteBits(luma, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cb, 8);
        for (int i = 0; i < 64; i++) bits.WriteBits(cr, 8);
        return bits.ToRbsp();
    }

    private static byte[] BuildNal(byte header, byte[] rbsp)
    {
        List<byte> nal = [header];
        int zeroCount = 0;
        foreach (byte value in rbsp)
        {
            if (zeroCount >= 2 && value <= 3)
            {
                nal.Add(3);
                zeroCount = 0;
            }
            nal.Add(value);
            zeroCount = value == 0 ? zeroCount + 1 : 0;
        }
        return nal.ToArray();
    }

    private static void AddLengthPrefixedNal(List<byte> destination, byte[] nal)
    {
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)nal.Length));
        destination.Add(length[0]);
        destination.Add(length[1]);
        destination.AddRange(nal);
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

        public void AlignWithZeroes()
        {
            while (_bits.Count % 8 != 0)
                _bits.Add(false);
        }

        public byte[] ToRbsp()
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

using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Reads the SPS syntax needed to establish H.264 picture geometry and decoding state.</summary>
public sealed class H264SequenceParameterSetReader
{
    private static readonly HashSet<byte> ExtendedProfiles =
    [
        44, 83, 86, 100, 110, 118, 122, 128, 134, 135, 138, 139, 144, 244
    ];

    public H264SequenceParameterSet Read(H264NalUnit nalUnit)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        if (nalUnit.NalUnitType != 7)
            throw new FormatException("The NAL unit is not an SPS.");

        H264BitReader reader = new(nalUnit.Rbsp);
        byte profileIdc = (byte)reader.ReadBits(8);
        _ = reader.ReadBits(8); // constraint flags and reserved_zero_2bits
        byte levelIdc = (byte)reader.ReadBits(8);
        uint spsId = ReadBoundedUe(reader, 31, "seq_parameter_set_id");

        uint chromaFormatIdc = 1;
        bool separateColourPlaneFlag = false;
        uint bitDepthLumaMinus8 = 0;
        uint bitDepthChromaMinus8 = 0;

        if (ExtendedProfiles.Contains(profileIdc))
        {
            chromaFormatIdc = ReadBoundedUe(reader, 3, "chroma_format_idc");
            if (chromaFormatIdc == 3)
                separateColourPlaneFlag = reader.ReadFlag();

            bitDepthLumaMinus8 = ReadBoundedUe(reader, 6, "bit_depth_luma_minus8");
            bitDepthChromaMinus8 = ReadBoundedUe(reader, 6, "bit_depth_chroma_minus8");
            _ = reader.ReadFlag(); // qpprime_y_zero_transform_bypass_flag

            if (reader.ReadFlag())
            {
                int scalingListCount = chromaFormatIdc == 3 ? 12 : 8;
                for (int i = 0; i < scalingListCount; i++)
                {
                    if (reader.ReadFlag())
                        SkipScalingList(reader, i < 6 ? 16 : 64);
                }
            }
        }

        uint log2MaxFrameNumMinus4 = ReadBoundedUe(reader, 12, "log2_max_frame_num_minus4");
        uint picOrderCntType = ReadBoundedUe(reader, 2, "pic_order_cnt_type");
        uint log2MaxPicOrderCntLsbMinus4 = 0;

        if (picOrderCntType == 0)
        {
            log2MaxPicOrderCntLsbMinus4 =
                ReadBoundedUe(reader, 12, "log2_max_pic_order_cnt_lsb_minus4");
        }
        else if (picOrderCntType == 1)
        {
            _ = reader.ReadFlag(); // delta_pic_order_always_zero_flag
            _ = reader.ReadSignedExpGolomb();
            _ = reader.ReadSignedExpGolomb();
            uint cycleCount = ReadBoundedUe(reader, 255, "num_ref_frames_in_pic_order_cnt_cycle");
            for (uint i = 0; i < cycleCount; i++)
                _ = reader.ReadSignedExpGolomb();
        }

        uint maxNumRefFrames = ReadBoundedUe(reader, 65535, "max_num_ref_frames");
        _ = reader.ReadFlag(); // gaps_in_frame_num_value_allowed_flag
        uint widthInMbsMinus1 = ReadBoundedUe(reader, 65535, "pic_width_in_mbs_minus1");
        uint heightInMapUnitsMinus1 = ReadBoundedUe(reader, 65535, "pic_height_in_map_units_minus1");
        bool frameMbsOnly = reader.ReadFlag();

        if (!frameMbsOnly)
            _ = reader.ReadFlag(); // mb_adaptive_frame_field_flag

        bool direct8X8Inference = reader.ReadFlag();
        uint cropLeft = 0, cropRight = 0, cropTop = 0, cropBottom = 0;

        if (reader.ReadFlag())
        {
            cropLeft = ReadBoundedUe(reader, 1_000_000, "frame_crop_left_offset");
            cropRight = ReadBoundedUe(reader, 1_000_000, "frame_crop_right_offset");
            cropTop = ReadBoundedUe(reader, 1_000_000, "frame_crop_top_offset");
            cropBottom = ReadBoundedUe(reader, 1_000_000, "frame_crop_bottom_offset");
        }

        bool vuiPresent = reader.ReadFlag();

        uint chromaArrayType = separateColourPlaneFlag ? 0 : chromaFormatIdc;
        uint subWidth = chromaArrayType is 1 or 2 ? (chromaArrayType == 1 ? 2u : 2u) : 1u;
        uint subHeight = chromaArrayType == 1 ? 2u : 1u;
        uint cropUnitX = chromaArrayType == 0 ? 1u : subWidth;
        uint cropUnitY = chromaArrayType == 0 ? (frameMbsOnly ? 1u : 2u) : subHeight * (frameMbsOnly ? 1u : 2u);

        ulong codedWidth = checked(((ulong)widthInMbsMinus1 + 1) * 16);
        ulong codedHeight = checked(((ulong)heightInMapUnitsMinus1 + 1) * 16 * (frameMbsOnly ? 1UL : 2UL));
        ulong cropWidth = checked(((ulong)cropLeft + cropRight) * cropUnitX);
        ulong cropHeight = checked(((ulong)cropTop + cropBottom) * cropUnitY);

        if (cropWidth >= codedWidth || cropHeight >= codedHeight)
            throw new FormatException("SPS cropping removes the entire coded picture.");

        uint frameWidth = checked((uint)(codedWidth - cropWidth));
        uint frameHeight = checked((uint)(codedHeight - cropHeight));

        return new H264SequenceParameterSet(
            spsId, profileIdc, levelIdc, chromaFormatIdc,
            separateColourPlaneFlag, bitDepthLumaMinus8,
            bitDepthChromaMinus8, log2MaxFrameNumMinus4,
            picOrderCntType, log2MaxPicOrderCntLsbMinus4,
            maxNumRefFrames, widthInMbsMinus1,
            heightInMapUnitsMinus1, frameMbsOnly,
            direct8X8Inference, frameWidth, frameHeight, vuiPresent);
    }

    private static uint ReadBoundedUe(H264BitReader reader, uint maximum, string field)
    {
        uint value = reader.ReadUnsignedExpGolomb();
        if (value > maximum)
            throw new FormatException($"SPS {field} is outside its permitted range.");
        return value;
    }

    private static void SkipScalingList(H264BitReader reader, int size)
    {
        int lastScale = 8;
        int nextScale = 8;
        for (int i = 0; i < size; i++)
        {
            if (nextScale != 0)
            {
                int deltaScale = reader.ReadSignedExpGolomb();
                nextScale = (lastScale + deltaScale + 256) % 256;
            }
            lastScale = nextScale == 0 ? lastScale : nextScale;
        }
    }
}

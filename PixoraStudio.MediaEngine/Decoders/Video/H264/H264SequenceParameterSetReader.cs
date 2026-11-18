using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Parses H.264 sequence parameter sets, including geometry and VUI/HRD metadata.</summary>
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
        byte constraints = (byte)reader.ReadBits(8);
        if ((constraints & 0x03) != 0)
            throw new FormatException("SPS reserved_zero_2bits must be zero.");
        byte levelIdc = (byte)reader.ReadBits(8);
        uint spsId = ReadBoundedUe(reader, 31, "seq_parameter_set_id");

        uint chromaFormatIdc = 1;
        bool separateColourPlaneFlag = false;
        uint bitDepthLumaMinus8 = 0;
        uint bitDepthChromaMinus8 = 0;

        bool scalingMatrixPresent = false;
        List<IReadOnlyList<int>> scalingLists = [];
        List<bool> scalingListUseDefault = [];

        if (ExtendedProfiles.Contains(profileIdc))
        {
            chromaFormatIdc = ReadBoundedUe(reader, 3, "chroma_format_idc");
            if (chromaFormatIdc == 3)
                separateColourPlaneFlag = reader.ReadFlag();

            bitDepthLumaMinus8 = ReadBoundedUe(reader, 6, "bit_depth_luma_minus8");
            bitDepthChromaMinus8 = ReadBoundedUe(reader, 6, "bit_depth_chroma_minus8");
            _ = reader.ReadFlag();
            scalingMatrixPresent = reader.ReadFlag();
            if (scalingMatrixPresent)
            {
                int scalingListCount = chromaFormatIdc == 3 ? 12 : 8;
                for (int i = 0; i < scalingListCount; i++)
                {
                    if (reader.ReadFlag())
                    {
                        int[] list = ReadScalingList(reader, i < 6 ? 16 : 64, out bool useDefault);
                        scalingLists.Add(list);
                        scalingListUseDefault.Add(useDefault);
                    }
                    else
                    {
                        scalingLists.Add([]);
                        scalingListUseDefault.Add(false);
                    }
                }
            }
        }

        uint log2MaxFrameNumMinus4 = ReadBoundedUe(reader, 12, "log2_max_frame_num_minus4");
        uint picOrderCntType = ReadBoundedUe(reader, 2, "pic_order_cnt_type");
        uint log2MaxPicOrderCntLsbMinus4 = 0;
        bool deltaPicOrderAlwaysZero = false;
        int offsetForNonRefPic = 0, offsetForTopToBottomField = 0;
        List<int> offsetForRefFrame = [];
        if (picOrderCntType == 0)
            log2MaxPicOrderCntLsbMinus4 = ReadBoundedUe(reader, 12, "log2_max_pic_order_cnt_lsb_minus4");
        else if (picOrderCntType == 1)
        {
            deltaPicOrderAlwaysZero = reader.ReadFlag();
            offsetForNonRefPic = reader.ReadSignedExpGolomb();
            offsetForTopToBottomField = reader.ReadSignedExpGolomb();
            uint cycleCount = ReadBoundedUe(reader, 255, "num_ref_frames_in_pic_order_cnt_cycle");
            for (uint i = 0; i < cycleCount; i++) offsetForRefFrame.Add(reader.ReadSignedExpGolomb());
        }

        uint maxNumRefFrames = ReadBoundedUe(reader, 65535, "max_num_ref_frames");
        bool gapsInFrameNumAllowed = reader.ReadFlag();
        uint widthInMbsMinus1 = ReadBoundedUe(reader, 65535, "pic_width_in_mbs_minus1");
        uint heightInMapUnitsMinus1 = ReadBoundedUe(reader, 65535, "pic_height_in_map_units_minus1");
        bool frameMbsOnly = reader.ReadFlag();
        bool mbAdaptiveFrameField = !frameMbsOnly && reader.ReadFlag();
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
        H264VuiParameters? vui = vuiPresent ? ReadVui(reader) : null;

        reader.ConsumeRbspTrailingBits();

        uint chromaArrayType = separateColourPlaneFlag ? 0 : chromaFormatIdc;
        uint subWidth = chromaArrayType is 1 or 2 ? 2u : 1u;
        uint subHeight = chromaArrayType == 1 ? 2u : 1u;
        uint cropUnitX = chromaArrayType == 0 ? 1u : subWidth;
        uint cropUnitY = chromaArrayType == 0 ? (frameMbsOnly ? 1u : 2u) : subHeight * (frameMbsOnly ? 1u : 2u);

        ulong codedWidth = checked(((ulong)widthInMbsMinus1 + 1) * 16);
        ulong codedHeight = checked(((ulong)heightInMapUnitsMinus1 + 1) * 16 * (frameMbsOnly ? 1UL : 2UL));
        ulong cropWidth = checked(((ulong)cropLeft + cropRight) * cropUnitX);
        ulong cropHeight = checked(((ulong)cropTop + cropBottom) * cropUnitY);
        if (cropWidth >= codedWidth || cropHeight >= codedHeight)
            throw new FormatException("SPS cropping removes the entire coded picture.");

        return new H264SequenceParameterSet(
            spsId, profileIdc, levelIdc, chromaFormatIdc,
            separateColourPlaneFlag, bitDepthLumaMinus8,
            bitDepthChromaMinus8, log2MaxFrameNumMinus4,
            picOrderCntType, log2MaxPicOrderCntLsbMinus4,
            maxNumRefFrames, widthInMbsMinus1,
            heightInMapUnitsMinus1, frameMbsOnly,
            direct8X8Inference, checked((uint)(codedWidth - cropWidth)),
            checked((uint)(codedHeight - cropHeight)), vuiPresent)
        {
            VuiParameters = vui,
            GapsInFrameNumValueAllowedFlag = gapsInFrameNumAllowed,
            MbAdaptiveFrameFieldFlag = mbAdaptiveFrameField,
            DeltaPicOrderAlwaysZeroFlag = deltaPicOrderAlwaysZero,
            OffsetForNonRefPic = offsetForNonRefPic,
            OffsetForTopToBottomField = offsetForTopToBottomField,
            OffsetForRefFrame = offsetForRefFrame,
            ScalingMatrixPresentFlag = scalingMatrixPresent,
            ScalingLists = scalingLists,
            ScalingListUseDefault = scalingListUseDefault
        };
    }

    private static H264VuiParameters ReadVui(H264BitReader reader)
    {
        uint aspectRatioIdc = reader.ReadBits(8);
        uint? sarWidth = null, sarHeight = null;
        if (aspectRatioIdc == 255)
        {
            sarWidth = reader.ReadBits(16);
            sarHeight = reader.ReadBits(16);
            if (sarWidth == 0 || sarHeight == 0)
                throw new FormatException("Extended sample aspect ratio must be non-zero.");
        }

        bool overscanAppropriate = false;
        if (reader.ReadFlag()) overscanAppropriate = reader.ReadFlag();

        uint? videoFormat = null, colourPrimaries = null, transfer = null, matrix = null;
        bool? fullRange = null;
        if (reader.ReadFlag())
        {
            videoFormat = reader.ReadBits(3);
            fullRange = reader.ReadFlag();
            if (reader.ReadFlag())
            {
                colourPrimaries = reader.ReadBits(8);
                transfer = reader.ReadBits(8);
                matrix = reader.ReadBits(8);
            }
        }

        uint? chromaTop = null, chromaBottom = null;
        if (reader.ReadFlag())
        {
            chromaTop = ReadBoundedUe(reader, 5, "chroma_sample_loc_type_top_field");
            chromaBottom = ReadBoundedUe(reader, 5, "chroma_sample_loc_type_bottom_field");
        }

        uint? numUnitsInTick = null, timeScale = null;
        bool fixedFrameRate = false;
        if (reader.ReadFlag())
        {
            numUnitsInTick = reader.ReadBits(32);
            timeScale = reader.ReadBits(32);
            fixedFrameRate = reader.ReadFlag();
            if (numUnitsInTick == 0 || timeScale == 0)
                throw new FormatException("VUI timing values must be non-zero.");
        }

        H264HrdParameters? nalHrd = reader.ReadFlag() ? ReadHrd(reader) : null;
        H264HrdParameters? vclHrd = reader.ReadFlag() ? ReadHrd(reader) : null;
        bool lowDelay = false;
        if (nalHrd is not null || vclHrd is not null) lowDelay = reader.ReadFlag();

        bool picStruct = reader.ReadFlag();
        bool restrictionPresent = reader.ReadFlag();
        bool motionVectors = false;
        uint? maxBytes = null, maxBits = null, maxMvH = null, maxMvV = null, maxReorder = null, maxBuffer = null;
        if (restrictionPresent)
        {
            motionVectors = reader.ReadFlag();
            maxBytes = ReadBoundedUe(reader, 16, "max_bytes_per_pic_denom");
            maxBits = ReadBoundedUe(reader, 16, "max_bits_per_mb_denom");
            maxMvH = ReadBoundedUe(reader, 16, "log2_max_mv_length_horizontal");
            maxMvV = ReadBoundedUe(reader, 16, "log2_max_mv_length_vertical");
            maxReorder = ReadBoundedUe(reader, 65535, "max_num_reorder_frames");
            maxBuffer = ReadBoundedUe(reader, 65535, "max_dec_frame_buffering");
            if (maxReorder > maxBuffer)
                throw new FormatException("VUI max_num_reorder_frames exceeds max_dec_frame_buffering.");
        }

        return new H264VuiParameters(
            aspectRatioIdc, sarWidth, sarHeight, overscanAppropriate,
            videoFormat, fullRange, colourPrimaries, transfer, matrix,
            chromaTop, chromaBottom, numUnitsInTick, timeScale,
            fixedFrameRate, nalHrd, vclHrd, lowDelay, picStruct,
            restrictionPresent, motionVectors, maxBytes, maxBits,
            maxMvH, maxMvV, maxReorder, maxBuffer);
    }

    private static H264HrdParameters ReadHrd(H264BitReader reader)
    {
        uint cpbCountMinus1 = ReadBoundedUe(reader, 31, "cpb_cnt_minus1");
        byte bitRateScale = (byte)reader.ReadBits(4);
        byte cpbSizeScale = (byte)reader.ReadBits(4);
        List<H264HrdCpbEntry> entries = new(checked((int)cpbCountMinus1 + 1));
        for (uint i = 0; i <= cpbCountMinus1; i++)
        {
            uint bitRate = reader.ReadUnsignedExpGolomb();
            uint cpbSize = reader.ReadUnsignedExpGolomb();
            bool cbr = reader.ReadFlag();
            entries.Add(new H264HrdCpbEntry(bitRate, cpbSize, cbr));
        }

        return new H264HrdParameters(
            cpbCountMinus1 + 1, bitRateScale, cpbSizeScale, entries,
            (byte)reader.ReadBits(5), (byte)reader.ReadBits(5),
            (byte)reader.ReadBits(5), (byte)reader.ReadBits(5));
    }

    private static uint ReadBoundedUe(H264BitReader reader, uint maximum, string field)
    {
        uint value = reader.ReadUnsignedExpGolomb();
        if (value > maximum) throw new FormatException($"SPS {field} is outside its permitted range.");
        return value;
    }

    private static int[] ReadScalingList(H264BitReader reader, int size, out bool useDefault)
    {
        int[] values = new int[size];
        int lastScale = 8, nextScale = 8;
        useDefault = false;
        for (int i = 0; i < size; i++)
        {
            if (nextScale != 0)
            {
                int deltaScale = reader.ReadSignedExpGolomb();
                if (deltaScale is < -128 or > 127)
                    throw new FormatException("Scaling-list delta_scale is outside the permitted range.");
                nextScale = (lastScale + deltaScale + 256) % 256;
            }
            if (i == 0 && nextScale == 0) useDefault = true;
            int scale = nextScale == 0 ? lastScale : nextScale;
            values[i] = scale;
            lastScale = scale;
        }
        return values;
    }
}

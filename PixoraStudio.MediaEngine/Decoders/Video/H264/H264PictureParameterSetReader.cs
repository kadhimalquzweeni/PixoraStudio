using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Parses the complete PPS syntax, including FMO maps and optional scaling matrices.</summary>
public sealed class H264PictureParameterSetReader
{
    /// <summary>
    /// Reads only the leading PPS and SPS identifiers. This permits callers
    /// to resolve the SPS context before parsing optional scaling-list syntax.
    /// </summary>
    public (uint PictureParameterSetId, uint SequenceParameterSetId)
        ReadParameterSetIdentifiers(H264NalUnit nalUnit)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        if (nalUnit.NalUnitType != 8)
            throw new FormatException("The NAL unit is not a PPS.");

        H264BitReader reader = new(nalUnit.Rbsp);
        uint ppsId = ReadBoundedUe(reader, 255, "pic_parameter_set_id");
        uint spsId = ReadBoundedUe(reader, 31, "seq_parameter_set_id");
        return (ppsId, spsId);
    }

    public H264PictureParameterSetHeader Read(
        H264NalUnit nalUnit,
        H264SequenceParameterSet? sequenceParameterSet = null)
    {
        try
        {
            return ReadCore(nalUnit, sequenceParameterSet);
        }
        catch (EndOfStreamException exception)
        {
            throw new FormatException(
                "The PPS RBSP is truncated or has invalid trailing bits.", exception);
        }
    }

    private H264PictureParameterSetHeader ReadCore(
        H264NalUnit nalUnit,
        H264SequenceParameterSet? sequenceParameterSet)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        if (nalUnit.NalUnitType != 8)
            throw new FormatException("The NAL unit is not a PPS.");

        H264BitReader reader = new(nalUnit.Rbsp);
        uint ppsId = ReadBoundedUe(reader, 255, "pic_parameter_set_id");
        uint spsId = ReadBoundedUe(reader, 31, "seq_parameter_set_id");
        bool entropy = reader.ReadFlag();
        bool bottomFieldPoc = reader.ReadFlag();
        uint groupsMinus1 = ReadBoundedUe(reader, 7, "num_slice_groups_minus1");

        uint? mapType = null, changeRate = null, mapUnitsMinus1 = null;
        bool? changeDirection = null;
        List<uint> runLengths = [];
        List<H264SliceGroupRectangle> rectangles = [];
        List<uint> groupIds = [];

        if (groupsMinus1 > 0)
        {
            mapType = ReadBoundedUe(reader, 6, "slice_group_map_type");
            switch (mapType.Value)
            {
                case 0:
                    for (uint i = 0; i <= groupsMinus1; i++)
                        runLengths.Add(ReadBoundedUe(reader, 1_000_000, "run_length_minus1"));
                    break;
                case 1:
                    // Dispersed map type: the map is derived and carries no additional PPS syntax.
                    break;
                case 2:
                    for (uint i = 0; i < groupsMinus1; i++)
                    {
                        uint topLeft = ReadBoundedUe(reader, 1_000_000, "top_left");
                        uint bottomRight = ReadBoundedUe(reader, 1_000_000, "bottom_right");
                        if (bottomRight < topLeft)
                            throw new FormatException("FMO slice group rectangle has inverted bounds.");
                        rectangles.Add(new H264SliceGroupRectangle(topLeft, bottomRight));
                    }
                    break;
                case 3:
                case 4:
                case 5:
                    changeDirection = reader.ReadFlag();
                    changeRate = ReadBoundedUe(reader, 1_000_000, "slice_group_change_rate_minus1");
                    break;
                case 6:
                    mapUnitsMinus1 = ReadBoundedUe(reader, 1_000_000, "pic_size_in_map_units_minus1");
                    int bitsPerGroup = CeilLog2(groupsMinus1 + 1);
                    for (uint i = 0; i <= mapUnitsMinus1; i++)
                    {
                        uint groupId = reader.ReadBits(bitsPerGroup);
                        if (groupId > groupsMinus1)
                            throw new FormatException("FMO slice_group_id exceeds num_slice_groups_minus1.");
                        groupIds.Add(groupId);
                    }
                    break;
                default:
                    throw new FormatException("Unsupported FMO slice_group_map_type.");
            }
        }

        uint l0 = ReadBoundedUe(reader, 31, "num_ref_idx_l0_default_active_minus1");
        uint l1 = ReadBoundedUe(reader, 31, "num_ref_idx_l1_default_active_minus1");
        bool weightedPred = reader.ReadFlag();
        uint weightedBipred = reader.ReadBits(2);
        if (weightedBipred > 2)
            throw new FormatException("weighted_bipred_idc value 3 is reserved.");
        int initQp = ReadBoundedSe(reader, -26, 25, "pic_init_qp_minus26");
        int initQs = ReadBoundedSe(reader, -26, 25, "pic_init_qs_minus26");
        int chromaOffset = ReadBoundedSe(reader, -12, 12, "chroma_qp_index_offset");
        bool deblocking = reader.ReadFlag();
        bool constrainedIntra = reader.ReadFlag();
        bool redundant = reader.ReadFlag();

        bool hasExtension = MoreRbspData(reader);
        bool transform8x8 = false, scalingPresent = false;
        List<IReadOnlyList<int>> scalingLists = [];
        List<bool> scalingListUseDefault = [];
        int? secondChromaOffset = null;
        if (hasExtension)
        {
            transform8x8 = reader.ReadFlag();
            scalingPresent = reader.ReadFlag();
            if (scalingPresent)
            {
                uint chromaFormat = sequenceParameterSet?.ChromaFormatIdc ?? 1;
                int listCount = 6 + (transform8x8 ? (chromaFormat == 3 ? 6 : 2) : 0);
                for (int i = 0; i < listCount; i++)
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
            secondChromaOffset = ReadBoundedSe(reader, -12, 12, "second_chroma_qp_index_offset");
        }

        reader.ConsumeRbspTrailingBits();

        return new H264PictureParameterSetHeader(
            ppsId, spsId, entropy, bottomFieldPoc, groupsMinus1)
        {
            SliceGroupMapType = mapType,
            RunLengthMinus1 = runLengths,
            SliceGroupRectangles = rectangles,
            SliceGroupChangeDirectionFlag = changeDirection,
            SliceGroupChangeRateMinus1 = changeRate,
            PicSizeInMapUnitsMinus1 = mapUnitsMinus1,
            SliceGroupId = groupIds,
            NumRefIdxL0DefaultActiveMinus1 = l0,
            NumRefIdxL1DefaultActiveMinus1 = l1,
            WeightedPredFlag = weightedPred,
            WeightedBipredIdc = (byte)weightedBipred,
            PicInitQpMinus26 = initQp,
            PicInitQsMinus26 = initQs,
            ChromaQpIndexOffset = chromaOffset,
            DeblockingFilterControlPresentFlag = deblocking,
            ConstrainedIntraPredFlag = constrainedIntra,
            RedundantPicCntPresentFlag = redundant,
            HasExtension = hasExtension,
            Transform8X8ModeFlag = transform8x8,
            PicScalingMatrixPresentFlag = scalingPresent,
            ScalingLists = scalingLists,
            ScalingListUseDefault = scalingListUseDefault,
            SecondChromaQpIndexOffset = secondChromaOffset
        };
    }

    private static bool MoreRbspData(H264BitReader reader)
    {
        if (reader.RemainingBits == 0) return false;

        // Do not consume bits while distinguishing an optional PPS extension
        // from rbsp_trailing_bits. The first extension syntax element may itself
        // begin with a one bit (for example transform_8x8_mode_flag).
        if (reader.PeekBits(1) == 0) return true;

        // rbsp_trailing_bits is one stop bit followed only by zero alignment bits.
        for (int offset = 1; offset < reader.RemainingBits; offset++)
            if (reader.PeekBits(1, offset) != 0) return true;

        return false;
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

    private static int CeilLog2(uint value)
    {
        int bits = 0;
        uint v = value - 1;
        while (v != 0) { bits++; v >>= 1; }
        return bits;
    }

    private static uint ReadBoundedUe(H264BitReader reader, uint maximum, string field)
    {
        uint value = reader.ReadUnsignedExpGolomb();
        if (value > maximum) throw new FormatException($"PPS {field} is outside its permitted range.");
        return value;
    }

    private static int ReadBoundedSe(H264BitReader reader, int minimum, int maximum, string field)
    {
        int value = reader.ReadSignedExpGolomb();
        if (value < minimum || value > maximum)
            throw new FormatException($"PPS {field} is outside its permitted range.");
        return value;
    }
}

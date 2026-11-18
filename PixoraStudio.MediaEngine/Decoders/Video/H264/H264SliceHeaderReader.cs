using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>Parses AVC slice headers. Slice payload (macroblock data) remains unread.</summary>
public sealed class H264SliceHeaderReader
{
    public H264SliceHeader Read(
        H264NalUnit nalUnit,
        H264SequenceParameterSet sequenceParameterSet,
        H264PictureParameterSetHeader pictureParameterSet)
    {
        ArgumentNullException.ThrowIfNull(nalUnit);
        ArgumentNullException.ThrowIfNull(sequenceParameterSet);
        ArgumentNullException.ThrowIfNull(pictureParameterSet);

        if (nalUnit.NalUnitType is not (1 or 5))
            throw new FormatException("Only non-IDR and IDR coded slice NAL units are supported.");
        if (pictureParameterSet.SequenceParameterSetId != sequenceParameterSet.Id)
            throw new FormatException("The PPS references a different SPS.");

        H264BitReader reader = new(nalUnit.Rbsp);
        uint firstMb = ReadBoundedUe(reader, 1_000_000, "first_mb_in_slice");
        uint sliceTypeCode = ReadBoundedUe(reader, 9, "slice_type");
        H264SliceType sliceType = (H264SliceType)(sliceTypeCode % 5);
        uint ppsId = ReadBoundedUe(reader, 255, "pic_parameter_set_id");
        if (ppsId != pictureParameterSet.Id)
            throw new FormatException("Slice pic_parameter_set_id does not match the supplied PPS.");

        uint colourPlaneId = 0;
        if (sequenceParameterSet.SeparateColourPlaneFlag)
        {
            colourPlaneId = reader.ReadBits(2);
            if (colourPlaneId > 2) throw new FormatException("colour_plane_id value 3 is reserved.");
        }

        int frameNumBits = checked((int)sequenceParameterSet.Log2MaxFrameNumMinus4 + 4);
        uint frameNum = reader.ReadBits(frameNumBits);
        bool fieldPic = false, bottomField = false;
        if (!sequenceParameterSet.FrameMbsOnlyFlag)
        {
            fieldPic = reader.ReadFlag();
            if (fieldPic) bottomField = reader.ReadFlag();
        }

        bool idr = nalUnit.NalUnitType == 5;
        if (idr && nalUnit.NalRefIdc == 0)
            throw new FormatException("An IDR slice must have a non-zero nal_ref_idc.");
        uint pictureSizeInMbs = checked((sequenceParameterSet.PicWidthInMbsMinus1 + 1) *
            (sequenceParameterSet.PicHeightInMapUnitsMinus1 + 1) *
            (sequenceParameterSet.FrameMbsOnlyFlag || fieldPic || sequenceParameterSet.MbAdaptiveFrameFieldFlag ? 1u : 2u));
        if (firstMb >= pictureSizeInMbs)
            throw new FormatException("first_mb_in_slice exceeds the picture macroblock count.");
        uint? idrPicId = idr ? ReadBoundedUe(reader, 65535, "idr_pic_id") : null;

        uint? picOrderCntLsb = null;
        int? deltaBottom = null, delta0 = null, delta1 = null;
        if (sequenceParameterSet.PicOrderCntType == 0)
        {
            picOrderCntLsb = reader.ReadBits(checked((int)sequenceParameterSet.Log2MaxPicOrderCntLsbMinus4 + 4));
            if (pictureParameterSet.BottomFieldPicOrderInFramePresentFlag && !fieldPic)
                deltaBottom = reader.ReadSignedExpGolomb();
        }
        else if (sequenceParameterSet.PicOrderCntType == 1 && !sequenceParameterSet.DeltaPicOrderAlwaysZeroFlag)
        {
            delta0 = reader.ReadSignedExpGolomb();
            if (pictureParameterSet.BottomFieldPicOrderInFramePresentFlag && !fieldPic)
                delta1 = reader.ReadSignedExpGolomb();
        }

        uint? redundantPicCnt = pictureParameterSet.RedundantPicCntPresentFlag
            ? ReadBoundedUe(reader, 127, "redundant_pic_cnt")
            : null;

        bool directSpatial = sliceType == H264SliceType.B && reader.ReadFlag();
        uint l0Minus1 = pictureParameterSet.NumRefIdxL0DefaultActiveMinus1;
        uint l1Minus1 = pictureParameterSet.NumRefIdxL1DefaultActiveMinus1;
        bool overrideRefs = false;
        if (sliceType is H264SliceType.P or H264SliceType.B or H264SliceType.Sp)
        {
            overrideRefs = reader.ReadFlag();
            if (overrideRefs)
            {
                l0Minus1 = ReadBoundedUe(reader, 31, "num_ref_idx_l0_active_minus1");
                if (sliceType == H264SliceType.B)
                    l1Minus1 = ReadBoundedUe(reader, 31, "num_ref_idx_l1_active_minus1");
            }
        }

        IReadOnlyList<H264ReferenceListModification> list0 = [];
        IReadOnlyList<H264ReferenceListModification> list1 = [];
        if (sliceType is not (H264SliceType.I or H264SliceType.Si))
        {
            list0 = ReadReferenceListModification(reader);
            if (sliceType == H264SliceType.B)
                list1 = ReadReferenceListModification(reader);
        }

        H264PredictionWeightTable? weights = null;
        if ((pictureParameterSet.WeightedPredFlag && sliceType is H264SliceType.P or H264SliceType.Sp) ||
            (pictureParameterSet.WeightedBipredIdc == 1 && sliceType == H264SliceType.B))
        {
            weights = ReadPredictionWeightTable(reader, sequenceParameterSet, sliceType, l0Minus1, l1Minus1);
        }

        H264DecodedReferenceMarking? marking = null;
        if (nalUnit.NalRefIdc != 0)
            marking = ReadDecodedReferenceMarking(reader, idr);

        uint? cabacInitIdc = null;
        if (pictureParameterSet.EntropyCodingModeFlag && sliceType is not (H264SliceType.I or H264SliceType.Si))
            cabacInitIdc = ReadBoundedUe(reader, 2, "cabac_init_idc");

        int qpBdOffsetY = checked((int)(6 * sequenceParameterSet.BitDepthLumaMinus8));
        int minSliceQpDelta = -26 - qpBdOffsetY - pictureParameterSet.PicInitQpMinus26;
        int maxSliceQpDelta = 25 - qpBdOffsetY - pictureParameterSet.PicInitQpMinus26;
        int sliceQpDelta = ReadBoundedSe(reader, minSliceQpDelta, maxSliceQpDelta, "slice_qp_delta");
        bool spForSwitch = false;
        int? sliceQsDelta = null;
        if (sliceType is H264SliceType.Sp or H264SliceType.Si)
        {
            if (sliceType == H264SliceType.Sp) spForSwitch = reader.ReadFlag();
            int minSliceQsDelta = -26 - pictureParameterSet.PicInitQsMinus26;
            int maxSliceQsDelta = 25 - pictureParameterSet.PicInitQsMinus26;
            sliceQsDelta = ReadBoundedSe(reader, minSliceQsDelta, maxSliceQsDelta, "slice_qs_delta");
        }

        uint? disableDeblocking = null;
        int? alphaOffset = null, betaOffset = null;
        if (pictureParameterSet.DeblockingFilterControlPresentFlag)
        {
            disableDeblocking = ReadBoundedUe(reader, 2, "disable_deblocking_filter_idc");
            if (disableDeblocking != 1)
            {
                alphaOffset = ReadBoundedSe(reader, -6, 6, "slice_alpha_c0_offset_div2");
                betaOffset = ReadBoundedSe(reader, -6, 6, "slice_beta_offset_div2");
            }
        }

        uint? sliceGroupChangeCycle = null;
        if (pictureParameterSet.NumSliceGroupsMinus1 > 0 &&
            pictureParameterSet.SliceGroupMapType is 3 or 4 or 5)
        {
            uint picSizeInMapUnits = checked((sequenceParameterSet.PicWidthInMbsMinus1 + 1) *
                (sequenceParameterSet.PicHeightInMapUnitsMinus1 + 1));
            if (pictureParameterSet.SliceGroupChangeRateMinus1 is null)
                throw new FormatException("FMO slice group change rate is missing from the PPS.");
            uint changeRate = checked(pictureParameterSet.SliceGroupChangeRateMinus1.Value + 1);
            uint maxCycle = picSizeInMapUnits / changeRate;
            int cycleBits = CeilLog2(maxCycle + 1);
            sliceGroupChangeCycle = reader.ReadBits(cycleBits);
            if (sliceGroupChangeCycle > maxCycle)
                throw new FormatException("slice_group_change_cycle exceeds the picture map range.");
        }

        return new H264SliceHeader(
            firstMb, sliceType, ppsId, colourPlaneId, frameNum, fieldPic, bottomField,
            idrPicId, picOrderCntLsb, deltaBottom, delta0, delta1, redundantPicCnt,
            directSpatial, overrideRefs, l0Minus1, l1Minus1, list0, list1, weights,
            marking, cabacInitIdc, sliceQpDelta, spForSwitch, sliceQsDelta,
            disableDeblocking, alphaOffset, betaOffset, sliceGroupChangeCycle);
    }

    private static IReadOnlyList<H264ReferenceListModification> ReadReferenceListModification(H264BitReader reader)
    {
        if (!reader.ReadFlag()) return [];
        List<H264ReferenceListModification> result = [];
        for (int i = 0; i < 64; i++)
        {
            uint idc = reader.ReadUnsignedExpGolomb();
            if (idc == 3) return result;
            if (idc > 2) throw new FormatException("ref_pic_list_modification idc is reserved.");
            uint value = reader.ReadUnsignedExpGolomb();
            result.Add(new H264ReferenceListModification(
                (byte)idc, idc is 0 or 1 ? value : null, idc == 2 ? value : null));
        }
        throw new FormatException("Reference picture list modification is missing its terminator.");
    }

    private static H264PredictionWeightTable ReadPredictionWeightTable(
        H264BitReader reader, H264SequenceParameterSet sps, H264SliceType type,
        uint l0Minus1, uint l1Minus1)
    {
        uint lumaDenom = ReadBoundedUe(reader, 7, "luma_log2_weight_denom");
        uint? chromaDenom = null;
        bool hasChroma = !sps.SeparateColourPlaneFlag && sps.ChromaFormatIdc != 0;
        if (hasChroma) chromaDenom = ReadBoundedUe(reader, 7, "chroma_log2_weight_denom");
        IReadOnlyList<H264PredictionWeight> l0 = ReadWeights(reader, l0Minus1 + 1, hasChroma);
        IReadOnlyList<H264PredictionWeight> l1 = type == H264SliceType.B
            ? ReadWeights(reader, l1Minus1 + 1, hasChroma) : [];
        return new H264PredictionWeightTable(lumaDenom, chromaDenom, l0, l1);
    }

    private static IReadOnlyList<H264PredictionWeight> ReadWeights(H264BitReader reader, uint count, bool hasChroma)
    {
        List<H264PredictionWeight> weights = new(checked((int)count));
        for (uint i = 0; i < count; i++)
        {
            bool lumaFlag = reader.ReadFlag();
            int? lumaWeight = null, lumaOffset = null;
            if (lumaFlag)
            {
                lumaWeight = ReadBoundedSe(reader, -128, 127, "luma_weight");
                lumaOffset = ReadBoundedSe(reader, -128, 127, "luma_offset");
            }

            bool chromaFlag = hasChroma && reader.ReadFlag();
            List<int> chromaWeights = [], chromaOffsets = [];
            if (chromaFlag)
            {
                for (int j = 0; j < 2; j++)
                {
                    chromaWeights.Add(ReadBoundedSe(reader, -128, 127, "chroma_weight"));
                    chromaOffsets.Add(ReadBoundedSe(reader, -128, 127, "chroma_offset"));
                }
            }
            weights.Add(new H264PredictionWeight(lumaFlag, lumaWeight, lumaOffset,
                chromaFlag, chromaWeights, chromaOffsets));
        }
        return weights;
    }

    private static H264DecodedReferenceMarking ReadDecodedReferenceMarking(H264BitReader reader, bool idr)
    {
        if (idr)
            return new H264DecodedReferenceMarking(reader.ReadFlag(), reader.ReadFlag(), false, []);

        bool adaptive = reader.ReadFlag();
        List<H264MemoryManagementControlOperation> operations = [];
        if (adaptive)
        {
            for (int i = 0; i < 64; i++)
            {
                uint op = reader.ReadUnsignedExpGolomb();
                if (op == 0)
                    return new H264DecodedReferenceMarking(false, false, true, operations);
                if (op > 6) throw new FormatException("memory_management_control_operation is reserved.");
                uint? difference = null, longTermPic = null, longTermFrame = null, maxLongTerm = null;
                switch (op)
                {
                    case 1:
                    case 3:
                        difference = reader.ReadUnsignedExpGolomb();
                        break;
                    case 2:
                        longTermPic = reader.ReadUnsignedExpGolomb();
                        break;
                }
                if (op is 3 or 6) longTermFrame = reader.ReadUnsignedExpGolomb();
                if (op == 4) maxLongTerm = reader.ReadUnsignedExpGolomb();
                operations.Add(new H264MemoryManagementControlOperation(op, difference, longTermPic, longTermFrame, maxLongTerm));
            }
            throw new FormatException("Decoded reference marking is missing its terminator.");
        }
        return new H264DecodedReferenceMarking(false, false, false, []);
    }

    private static int CeilLog2(uint value)
    {
        if (value <= 1) return 0;
        int bits = 0;
        for (uint v = value - 1; v != 0; v >>= 1) bits++;
        return bits;
    }

    private static uint ReadBoundedUe(H264BitReader reader, uint maximum, string field)
    {
        uint value = reader.ReadUnsignedExpGolomb();
        if (value > maximum) throw new FormatException($"Slice {field} is outside its permitted range.");
        return value;
    }

    private static int ReadBoundedSe(H264BitReader reader, int minimum, int maximum, string field)
    {
        int value = reader.ReadSignedExpGolomb();
        if (value < minimum || value > maximum)
            throw new FormatException($"Slice {field} is outside its permitted range.");
        return value;
    }
}

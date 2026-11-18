namespace PixoraStudio.MediaEngine.Models.Video;

public enum H264SliceType
{
    P = 0,
    B = 1,
    I = 2,
    Sp = 3,
    Si = 4
}

public sealed record H264SliceHeader(
    uint FirstMacroblockInSlice,
    H264SliceType SliceType,
    uint PicParameterSetId,
    uint ColourPlaneId,
    uint FrameNum,
    bool FieldPicFlag,
    bool BottomFieldFlag,
    uint? IdrPicId,
    uint? PicOrderCntLsb,
    int? DeltaPicOrderCntBottom,
    int? DeltaPicOrderCnt0,
    int? DeltaPicOrderCnt1,
    uint? RedundantPicCnt,
    bool DirectSpatialMvPredFlag,
    bool NumRefIdxActiveOverrideFlag,
    uint NumRefIdxL0ActiveMinus1,
    uint NumRefIdxL1ActiveMinus1,
    IReadOnlyList<H264ReferenceListModification> RefPicList0Modifications,
    IReadOnlyList<H264ReferenceListModification> RefPicList1Modifications,
    H264PredictionWeightTable? PredictionWeights,
    H264DecodedReferenceMarking? DecodedReferenceMarking,
    uint? CabacInitIdc,
    int SliceQpDelta,
    bool SpForSwitchFlag,
    uint? SliceQsDelta,
    uint? DisableDeblockingFilterIdc,
    int? SliceAlphaC0OffsetDiv2,
    int? SliceBetaOffsetDiv2,
    uint? SliceGroupChangeCycle);

public sealed record H264ReferenceListModification(byte ModificationOfPicNumsIdc, uint? AbsDiffPicNumMinus1, uint? LongTermPicNum);

public sealed record H264PredictionWeightTable(
    uint LumaLog2WeightDenom,
    uint? ChromaLog2WeightDenom,
    IReadOnlyList<H264PredictionWeight> List0,
    IReadOnlyList<H264PredictionWeight> List1);

public sealed record H264PredictionWeight(
    bool LumaWeightFlag,
    int? LumaWeight,
    int? LumaOffset,
    bool ChromaWeightFlag,
    IReadOnlyList<int> ChromaWeights,
    IReadOnlyList<int> ChromaOffsets);

public sealed record H264DecodedReferenceMarking(
    bool NoOutputOfPriorPicsFlag,
    bool LongTermReferenceFlag,
    bool AdaptiveRefPicMarkingModeFlag,
    IReadOnlyList<H264MemoryManagementControlOperation> Operations);

public sealed record H264MemoryManagementControlOperation(
    uint MemoryManagementControlOperation,
    uint? DifferenceOfPicNumsMinus1,
    uint? LongTermPicNum,
    uint? LongTermFrameIdx,
    uint? MaxLongTermFrameIdxPlus1);

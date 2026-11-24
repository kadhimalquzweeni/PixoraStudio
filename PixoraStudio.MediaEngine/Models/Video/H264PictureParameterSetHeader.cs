namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Complete parsed H.264 picture parameter set syntax.</summary>
public sealed record H264PictureParameterSetHeader(
    uint Id,
    uint SequenceParameterSetId,
    bool EntropyCodingModeFlag,
    bool BottomFieldPicOrderInFramePresentFlag,
    uint NumSliceGroupsMinus1)
{
    public uint? SliceGroupMapType { get; init; }
    public IReadOnlyList<uint> RunLengthMinus1 { get; init; } = [];
    public IReadOnlyList<H264SliceGroupRectangle> SliceGroupRectangles { get; init; } = [];
    public bool? SliceGroupChangeDirectionFlag { get; init; }
    public uint? SliceGroupChangeRateMinus1 { get; init; }
    public uint? PicSizeInMapUnitsMinus1 { get; init; }
    public IReadOnlyList<uint> SliceGroupId { get; init; } = [];
    public uint NumRefIdxL0DefaultActiveMinus1 { get; init; }
    public uint NumRefIdxL1DefaultActiveMinus1 { get; init; }
    public bool WeightedPredFlag { get; init; }
    public byte WeightedBipredIdc { get; init; }
    public int PicInitQpMinus26 { get; init; }
    public int PicInitQsMinus26 { get; init; }
    public int ChromaQpIndexOffset { get; init; }
    public bool DeblockingFilterControlPresentFlag { get; init; }
    public bool ConstrainedIntraPredFlag { get; init; }
    public bool RedundantPicCntPresentFlag { get; init; }
    public bool HasExtension { get; init; }
    public bool Transform8X8ModeFlag { get; init; }
    public bool PicScalingMatrixPresentFlag { get; init; }
    public IReadOnlyList<IReadOnlyList<int>> ScalingLists { get; init; } = [];
    public IReadOnlyList<bool> ScalingListUseDefault { get; init; } = [];
    public int? SecondChromaQpIndexOffset { get; init; }
}

public sealed record H264SliceGroupRectangle(uint TopLeft, uint BottomRight);

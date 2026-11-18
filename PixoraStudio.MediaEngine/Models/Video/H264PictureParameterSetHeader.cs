namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Core PPS identifiers and entropy/slice-group header fields.</summary>
public sealed record H264PictureParameterSetHeader(
    uint Id,
    uint SequenceParameterSetId,
    bool EntropyCodingModeFlag,
    bool BottomFieldPicOrderInFramePresentFlag,
    uint NumSliceGroupsMinus1);

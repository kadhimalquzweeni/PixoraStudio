namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Parsed sequence-level H.264 picture and frame geometry.</summary>
public sealed record H264SequenceParameterSet(
    uint Id,
    byte ProfileIdc,
    byte LevelIdc,
    uint ChromaFormatIdc,
    bool SeparateColourPlaneFlag,
    uint BitDepthLumaMinus8,
    uint BitDepthChromaMinus8,
    uint Log2MaxFrameNumMinus4,
    uint PicOrderCntType,
    uint Log2MaxPicOrderCntLsbMinus4,
    uint MaxNumRefFrames,
    uint PicWidthInMbsMinus1,
    uint PicHeightInMapUnitsMinus1,
    bool FrameMbsOnlyFlag,
    bool Direct8X8InferenceFlag,
    uint FrameWidth,
    uint FrameHeight,
    bool VuiParametersPresentFlag);

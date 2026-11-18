namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>Video usability information signalled by an H.264 sequence parameter set.</summary>
public sealed record H264VuiParameters(
    uint AspectRatioIdc,
    uint? SarWidth,
    uint? SarHeight,
    bool OverscanAppropriateFlag,
    uint? VideoFormat,
    bool? VideoFullRangeFlag,
    uint? ColourPrimaries,
    uint? TransferCharacteristics,
    uint? MatrixCoefficients,
    uint? ChromaSampleLocTypeTopField,
    uint? ChromaSampleLocTypeBottomField,
    uint? NumUnitsInTick,
    uint? TimeScale,
    bool FixedFrameRateFlag,
    H264HrdParameters? NalHrd,
    H264HrdParameters? VclHrd,
    bool LowDelayHrdFlag,
    bool PicStructPresentFlag,
    bool BitstreamRestrictionFlag,
    bool MotionVectorsOverPicBoundariesFlag,
    uint? MaxBytesPerPicDenom,
    uint? MaxBitsPerMbDenom,
    uint? Log2MaxMvLengthHorizontal,
    uint? Log2MaxMvLengthVertical,
    uint? MaxNumReorderFrames,
    uint? MaxDecFrameBuffering);

/// <summary>Hypothetical reference decoder parameters carried by VUI.</summary>
public sealed record H264HrdParameters(
    uint CpbCount,
    byte BitRateScale,
    byte CpbSizeScale,
    IReadOnlyList<H264HrdCpbEntry> Entries,
    byte InitialCpbRemovalDelayLengthMinus1,
    byte CpbRemovalDelayLengthMinus1,
    byte DpbOutputDelayLengthMinus1,
    byte TimeOffsetLength);

public sealed record H264HrdCpbEntry(
    uint BitRateValueMinus1,
    uint CpbSizeValueMinus1,
    bool CbrFlag);

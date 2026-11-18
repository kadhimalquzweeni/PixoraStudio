namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Parsed prev_intra*_pred_mode_flag and rem_intra*_pred_mode syntax for
/// the luma prediction blocks of an I_NxN macroblock.
/// </summary>
public sealed record H264IntraPredictionModeSyntax(
    bool Uses8X8Transform,
    IReadOnlyList<H264IntraPredictionModeEntry> Modes);

/// <summary>One explicitly coded or neighbour-derived intra prediction mode.</summary>
public sealed record H264IntraPredictionModeEntry(
    bool PreviousModeFlag,
    uint? RemainderMode);

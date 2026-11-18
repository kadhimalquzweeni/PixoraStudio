using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads the luma intra prediction mode syntax for I_NxN macroblocks.
/// The predicted mode itself depends on neighbouring macroblocks and is
/// deliberately derived by a later reconstruction stage.
/// </summary>
public sealed class H264IntraPredictionModeReader
{
    /// <summary>
    /// Reads sixteen 4x4 mode entries, or four 8x8 mode entries when the
    /// macroblock's transform_size_8x8_flag is set.
    /// </summary>
    public H264IntraPredictionModeSyntax Read(
        H264BitReader reader,
        bool transformSize8X8Flag)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int modeCount = transformSize8X8Flag ? 4 : 16;
        var modes = new H264IntraPredictionModeEntry[modeCount];

        for (int i = 0; i < modeCount; i++)
        {
            bool previousModeFlag = reader.ReadFlag();
            uint? remainderMode = previousModeFlag
                ? null
                : reader.ReadBits(3);

            modes[i] = new H264IntraPredictionModeEntry(
                previousModeFlag,
                remainderMode);
        }

        return new H264IntraPredictionModeSyntax(transformSize8X8Flag, modes);
    }

    /// <summary>
    /// Resolves rem_intra*_pred_mode against the predicted mode derived from
    /// neighbouring blocks, as specified by the H.264 mode-remapping rule.
    /// </summary>
    public static uint ResolveMode(
        bool previousModeFlag,
        uint? remainderMode,
        uint predictedMode)
    {
        if (predictedMode > 8)
            throw new ArgumentOutOfRangeException(nameof(predictedMode),
                "Intra prediction modes are in the range 0..8.");

        if (previousModeFlag)
        {
            if (remainderMode is not null)
                throw new ArgumentException(
                    "remainderMode must be null when prev_intra*_pred_mode_flag is true.",
                    nameof(remainderMode));
            return predictedMode;
        }

        if (remainderMode is null or > 7)
            throw new ArgumentOutOfRangeException(nameof(remainderMode),
                "rem_intra*_pred_mode must be in the range 0..7.");

        uint remainder = remainderMode.Value;
        return remainder < predictedMode ? remainder : remainder + 1;
    }
}

using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Derives H.264 decoded-picture-buffer limits from the SPS level and coded
/// picture dimensions, as required when VUI bitstream restrictions are absent.
/// </summary>
public static class H264DecodedPictureBufferLimits
{
    private static readonly IReadOnlyDictionary<byte, uint> MaxDpbMacroblocksByLevel =
        new Dictionary<byte, uint>
        {
            [9] = 396,   // Level 1b (for profiles that signal it with level_idc 9).
            [10] = 396,  // Level 1.0.
            [11] = 900,  // Level 1.1; Level 1b is handled separately below.
            [12] = 2376,
            [13] = 2376,
            [20] = 2376,
            [21] = 4752,
            [22] = 8100,
            [30] = 8100,
            [31] = 18000,
            [32] = 20480,
            [40] = 32768,
            [41] = 32768,
            [42] = 34816,
            [50] = 110400,
            [51] = 184320,
            [52] = 184320,
            [60] = 696320,
            [61] = 696320,
            [62] = 696320
        };

    /// <summary>
    /// Returns MaxDpbFrames = min(floor(MaxDpbMbs / PicSizeInMbs), 16).
    /// H.264 infers max_num_reorder_frames to this value when the VUI
    /// bitstream-restriction syntax is not present.
    /// </summary>
    public static int ResolveMaxDpbFrames(H264SequenceParameterSet sps)
    {
        ArgumentNullException.ThrowIfNull(sps);

        uint maxDpbMbs = ResolveMaxDpbMacroblocks(sps);
        ulong pictureSizeInMacroblocks;
        try
        {
            pictureSizeInMacroblocks = checked(
                ((ulong)sps.PicWidthInMbsMinus1 + 1) *
                ((ulong)sps.PicHeightInMapUnitsMinus1 + 1));
        }
        catch (OverflowException exception)
        {
            throw new FormatException("The SPS coded picture dimensions overflow the supported macroblock geometry.", exception);
        }

        if (pictureSizeInMacroblocks == 0 || pictureSizeInMacroblocks > maxDpbMbs)
            throw new FormatException(
                "The coded picture dimensions exceed the maximum decoded-picture buffer size for the SPS level.");

        ulong maxDpbFrames = Math.Min(16UL, maxDpbMbs / pictureSizeInMacroblocks);
        return checked((int)maxDpbFrames);
    }

    private static uint ResolveMaxDpbMacroblocks(H264SequenceParameterSet sps)
    {
        // Level 1b uses level_idc 11 plus constraint_set3_flag for
        // Baseline, Main and Extended profiles. Other profiles signal it
        // with level_idc 9.
        bool level1B =
            (sps.LevelIdc == 9) ||
            (sps.LevelIdc == 11 &&
             (sps.ProfileIdc is 66 or 77 or 88) &&
             (sps.ProfileCompatibility & 0x10) != 0);

        if (level1B)
            return 396;

        if (!MaxDpbMacroblocksByLevel.TryGetValue(sps.LevelIdc, out uint maxDpbMbs))
            throw new NotSupportedException(
                $"H.264 level_idc {sps.LevelIdc} is not supported for DPB limit derivation.");

        return maxDpbMbs;
    }
}

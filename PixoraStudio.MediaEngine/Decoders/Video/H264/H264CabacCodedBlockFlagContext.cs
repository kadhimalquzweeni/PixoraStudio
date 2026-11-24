namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Neighbour availability information used to derive the CABAC
/// coded_block_flag context increment for a transform block.
/// </summary>
public readonly record struct H264CabacCodedBlockNeighbour(
    bool MacroblockAvailable,
    bool TransformBlockAvailable,
    bool IsPcmMacroblock,
    bool CodedBlockFlag);

/// <summary>Derives the H.264 coded_block_flag context increment.</summary>
public static class H264CabacCodedBlockFlagContext
{
    /// <summary>
    /// Returns condTermFlagA + 2*condTermFlagB. For unavailable neighbours,
    /// intra current macroblocks infer condTermFlag=1 and inter current
    /// macroblocks infer zero. An available PCM neighbour contributes one.
    /// </summary>
    public static int GetContextIndexIncrement(
        bool currentMacroblockIsIntra,
        H264CabacCodedBlockNeighbour left,
        H264CabacCodedBlockNeighbour top)
    {
        int leftCondition = GetConditionFlag(currentMacroblockIsIntra, left);
        int topCondition = GetConditionFlag(currentMacroblockIsIntra, top);
        return leftCondition + (2 * topCondition);
    }

    private static int GetConditionFlag(
        bool currentMacroblockIsIntra,
        H264CabacCodedBlockNeighbour neighbour)
    {
        if (!neighbour.MacroblockAvailable)
            return currentMacroblockIsIntra ? 1 : 0;

        if (neighbour.IsPcmMacroblock)
            return 1;

        if (!neighbour.TransformBlockAvailable)
            return 0;

        return neighbour.CodedBlockFlag ? 1 : 0;
    }
}

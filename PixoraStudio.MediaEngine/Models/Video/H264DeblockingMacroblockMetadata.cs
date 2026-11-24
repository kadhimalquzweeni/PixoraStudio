namespace PixoraStudio.MediaEngine.Models.Video;

/// <summary>
/// Macroblock-level deblocking inputs. Block arrays use raster order; luma
/// always has 4x4 entries and chroma has 2x2 entries for 4:2:0 or 2x4 for 4:2:2.
/// </summary>
public sealed record H264DeblockingMacroblockMetadata(
    int QpY,
    int QpCb,
    int QpCr,
    int DisableDeblockingFilterIdc,
    int AlphaOffsetDiv2,
    int BetaOffsetDiv2,
    bool TransformSize8x8Flag,
    IReadOnlyList<H264DeblockingBlockMetadata> LumaBlocks,
    IReadOnlyList<H264DeblockingBlockMetadata> ChromaCbBlocks,
    IReadOnlyList<H264DeblockingBlockMetadata> ChromaCrBlocks)
{
    public H264DeblockingMacroblockMetadata DeepCopy() => this with
    {
        LumaBlocks = Array.AsReadOnly(LumaBlocks.ToArray()),
        ChromaCbBlocks = Array.AsReadOnly(ChromaCbBlocks.ToArray()),
        ChromaCrBlocks = Array.AsReadOnly(ChromaCrBlocks.ToArray())
    };

    public void Validate(int chromaFormatIdc)
    {
        if (QpY is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(QpY));
        if (QpCb is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(QpCb));
        if (QpCr is < 0 or > 51)
            throw new ArgumentOutOfRangeException(nameof(QpCr));
        if (DisableDeblockingFilterIdc is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(DisableDeblockingFilterIdc));
        if (AlphaOffsetDiv2 is < -6 or > 6)
            throw new ArgumentOutOfRangeException(nameof(AlphaOffsetDiv2));
        if (BetaOffsetDiv2 is < -6 or > 6)
            throw new ArgumentOutOfRangeException(nameof(BetaOffsetDiv2));
        int expectedChromaBlocks = chromaFormatIdc switch
        {
            1 => 4,
            2 => 8,
            _ => throw new NotSupportedException("Deblocking metadata supports 4:2:0 and 4:2:2 only.")
        };
        ValidateBlocks(LumaBlocks, 16, nameof(LumaBlocks));
        ValidateBlocks(ChromaCbBlocks, expectedChromaBlocks, nameof(ChromaCbBlocks));
        ValidateBlocks(ChromaCrBlocks, expectedChromaBlocks, nameof(ChromaCrBlocks));
    }

    private static void ValidateBlocks(
        IReadOnlyList<H264DeblockingBlockMetadata> blocks,
        int expectedCount,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(blocks, parameterName);
        if (blocks.Count != expectedCount)
            throw new ArgumentException(
                $"Expected {expectedCount} deblocking block entries.", parameterName);
        if (blocks.Any(block => block is null))
            throw new ArgumentException("Deblocking block entries cannot be null.", parameterName);
    }
}

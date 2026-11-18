namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Supplies the normative frame-coded CABAC residual context maps for
/// luma/chroma 4x4 and luma 8x8 transform blocks.
/// </summary>
public static class H264CabacResidualBlockContext
{
    private static readonly int[] Significant8X8 =
    [
        0, 1, 2, 3, 4, 5, 5, 4, 4, 3, 3, 4, 4, 4, 5, 5,
        4, 4, 4, 4, 3, 3, 6, 7, 7, 7, 8, 9, 10, 9, 8, 7,
        7, 6, 11, 12, 13, 11, 6, 7, 8, 9, 14, 10, 9, 8, 6, 11,
        12, 13, 11, 6, 9, 14, 10, 9, 11, 12, 13, 11, 14, 10, 12, 14
    ];

    private static readonly int[] LastSignificant8X8 =
    [
        0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4,
        5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8
    ];

    /// <summary>
    /// Creates context-index maps for ctxBlockCat 0..5. Categories 0..4
    /// use scan-position increments; category 5 uses the special 8x8 maps.
    /// </summary>
    public static H264CabacResidualBlockContextMap Create(int contextBlockCategory)
    {
        return contextBlockCategory switch
        {
            0 => CreateLinear(16, 105, 166, 227, 0),
            1 => CreateLinear(15, 120, 181, 237, 0),
            2 => CreateLinear(16, 134, 195, 247, 0),
            3 => CreateLinear(4, 149, 210, 257, 0),
            4 => CreateLinear(15, 152, 213, 266, 0),
            5 => new H264CabacResidualBlockContextMap(
                64, 0, 402, 417, 426,
                Significant8X8.ToArray(), LastSignificant8X8.ToArray(), true),
            _ => throw new ArgumentOutOfRangeException(nameof(contextBlockCategory),
                "Only frame-coded residual categories 0..5 are supported.")
        };
    }

    private static H264CabacResidualBlockContextMap CreateLinear(
        int maximumCoefficients,
        int significantOffset,
        int lastSignificantOffset,
        int absoluteLevelOffset,
        int startIndex)
    {
        int mapLength = maximumCoefficients - 1 - startIndex;
        int[] increments = Enumerable.Range(startIndex, mapLength).ToArray();
        return new H264CabacResidualBlockContextMap(
            maximumCoefficients, startIndex,
            significantOffset, lastSignificantOffset, absoluteLevelOffset,
            increments, increments.ToArray(), false);
    }
}

/// <summary>Complete CABAC context configuration for one residual block category.</summary>
public sealed record H264CabacResidualBlockContextMap(
    int MaximumCoefficients,
    int StartIndex,
    int SignificantContextOffset,
    int LastSignificantContextOffset,
    int AbsoluteLevelContextOffset,
    IReadOnlyList<int> SignificantContextIncrements,
    IReadOnlyList<int> LastSignificantContextIncrements,
    bool HasImplicitCodedBlockFlag);

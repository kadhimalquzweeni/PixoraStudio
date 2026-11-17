namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Describes the properties of an AAC spectral Huffman codebook.
/// </summary>
public sealed record AacSpectralCodebookInfo(
    int Index,
    int TupleSize,
    int MaximumMagnitude,
    bool IsSigned,
    bool UsesEscapeValues)
{
    public static AacSpectralCodebookInfo Get(int index)
    {
        return index switch
        {
            0 => new(0, 0, 0, false, false),
            1 => new(1, 4, 1, true, false),
            2 => new(2, 4, 1, true, false),
            3 => new(3, 4, 2, false, false),
            4 => new(4, 4, 2, false, false),
            5 => new(5, 2, 4, true, false),
            6 => new(6, 2, 4, true, false),
            7 => new(7, 2, 7, false, false),
            8 => new(8, 2, 7, false, false),
            9 => new(9, 2, 12, false, false),
            10 => new(10, 2, 12, false, false),
            11 => new(11, 2, 16, false, true),
            _ => throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "The AAC spectral codebook index is invalid.")
        };
    }
}
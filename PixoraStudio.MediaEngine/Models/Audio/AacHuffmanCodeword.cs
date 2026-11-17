namespace PixoraStudio.MediaEngine.Models.Audio;

/// <summary>
/// Represents one explicitly defined Huffman codeword.
/// </summary>
public readonly record struct AacHuffmanCodeword
{
    public uint Codeword { get; }
    public int BitLength { get; }
    public int Symbol { get; }

    public AacHuffmanCodeword(
        uint codeword,
        int bitLength,
        int symbol)
    {
        if (bitLength is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(bitLength));
        }

        if (bitLength < 32 && codeword >= (1u << bitLength))
        {
            throw new ArgumentOutOfRangeException(
                nameof(codeword),
                "The codeword does not fit within the specified bit length.");
        }

        Codeword = codeword;
        BitLength = bitLength;
        Symbol = symbol;
    }
}

using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Stores a prefix-free Huffman codebook and its decoding trie.
/// </summary>
public sealed class AacHuffmanCodebook
{
    private readonly AacHuffmanCodeword[] _codewords;
    private readonly IReadOnlyList<AacHuffmanCodeword> _readOnlyCodewords;

    internal HuffmanNode Root { get; } = new();

    public AacHuffmanCodebook(
        IEnumerable<AacHuffmanCodeword> codewords)
    {
        ArgumentNullException.ThrowIfNull(codewords);

        _codewords = codewords.ToArray();

        if (_codewords.Length == 0)
        {
            throw new ArgumentException(
                "A Huffman codebook must contain at least one codeword.",
                nameof(codewords));
        }

        _readOnlyCodewords = Array.AsReadOnly(_codewords);

        foreach (AacHuffmanCodeword codeword in _codewords)
        {
            Insert(codeword, nameof(codewords));
        }
    }

    public IReadOnlyList<AacHuffmanCodeword> Codewords =>
        _readOnlyCodewords;

    private void Insert(
        AacHuffmanCodeword codeword,
        string parameterName)
    {
        HuffmanNode current = Root;

        for (int bitIndex = codeword.BitLength - 1;
             bitIndex >= 0;
             bitIndex--)
        {
            if (current.HasSymbol)
            {
                throw new ArgumentException(
                    "The Huffman codebook contains a prefix conflict.",
                    parameterName);
            }

            bool one = ((codeword.Codeword >> bitIndex) & 1u) != 0;

            if (one)
            {
                current.One ??= new HuffmanNode();
                current = current.One;
            }
            else
            {
                current.Zero ??= new HuffmanNode();
                current = current.Zero;
            }
        }

        if (current.HasSymbol ||
            current.Zero is not null ||
            current.One is not null)
        {
            throw new ArgumentException(
                "The Huffman codebook contains a duplicate or prefix conflict.",
                parameterName);
        }

        current.Symbol = codeword.Symbol;
        current.HasSymbol = true;
    }

    internal sealed class HuffmanNode
    {
        public HuffmanNode? Zero { get; set; }

        public HuffmanNode? One { get; set; }

        public int Symbol { get; set; }

        public bool HasSymbol { get; set; }
    }
}


namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Decodes symbols from a prefix-free AAC Huffman codebook.
/// </summary>
public sealed class AacHuffmanDecoder
{
    public int DecodeSymbol(
        AacBitReader reader,
        AacHuffmanCodebook codebook)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(codebook);

        AacHuffmanCodebook.HuffmanNode current = codebook.Root;

        while (true)
        {
            uint bit = reader.ReadBits(1);

            AacHuffmanCodebook.HuffmanNode? next =
                bit == 0 ? current.Zero : current.One;

            if (next is null)
            {
                throw new InvalidDataException(
                    "The bitstream contains an invalid Huffman codeword.");
            }

            current = next;

            if (current.HasSymbol)
            {
                return current.Symbol;
            }
        }
    }
}

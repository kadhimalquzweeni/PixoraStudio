using PixoraStudio.MediaEngine.Models.Audio;

namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>
/// Decodes AAC spectral_data() tuples using the normative Huffman books.
/// </summary>
public sealed class AacSpectralDataDecoder
{
    private static readonly Lazy<AacHuffmanCodebook[]> Codebooks = new(() =>
        Enumerable.Range(1, 11)
            .Select(AacHuffmanCodebookFactory.CreateSpectralCodebook)
            .ToArray());

    private readonly AacHuffmanDecoder _huffmanDecoder = new();
    private readonly AacSpectralTupleDecoder _tupleDecoder = new();

    /// <summary>
    /// Decodes one spectral band. The coefficient count must be divisible by
    /// the tuple dimension of the selected spectral codebook.
    /// </summary>
    public int[] DecodeBand(
        AacBitReader reader,
        int codebookIndex,
        int coefficientCount)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (coefficientCount < 0)
            throw new ArgumentOutOfRangeException(nameof(coefficientCount));

        AacSpectralCodebookInfo info = AacSpectralCodebookInfo.Get(codebookIndex);
        if (codebookIndex == 0)
            return new int[coefficientCount];

        if (coefficientCount % info.TupleSize != 0)
            throw new ArgumentException(
                "The coefficient count must be divisible by the codebook tuple size.",
                nameof(coefficientCount));

        int[] coefficients = new int[coefficientCount];
        AacHuffmanCodebook codebook = Codebooks.Value[codebookIndex - 1];

        for (int offset = 0; offset < coefficientCount; offset += info.TupleSize)
        {
            int symbol = _huffmanDecoder.DecodeSymbol(reader, codebook);
            int[] tuple = _tupleDecoder.DecodeTuple(reader, codebookIndex, symbol);
            tuple.CopyTo(coefficients, offset);
        }

        return coefficients;
    }
}

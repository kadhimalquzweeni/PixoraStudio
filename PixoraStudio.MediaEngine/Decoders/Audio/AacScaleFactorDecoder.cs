namespace PixoraStudio.MediaEngine.Decoders.Audio;

/// <summary>Decodes AAC scalefactor Huffman deltas in the range -60..+60.</summary>
public sealed class AacScaleFactorDecoder
{
    private static readonly Lazy<AacHuffmanCodebook> Codebook = new(
        AacHuffmanCodebookFactory.CreateScaleFactorCodebook);

    private readonly AacHuffmanDecoder _decoder = new();

    public int DecodeDelta(AacBitReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return _decoder.DecodeSymbol(reader, Codebook.Value);
    }
}

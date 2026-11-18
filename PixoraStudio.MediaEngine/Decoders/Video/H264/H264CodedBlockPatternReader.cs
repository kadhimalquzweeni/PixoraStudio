using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Reads and maps the coded_block_pattern syntax for intra 4x4/8x8
/// macroblocks using Table 9-4 of ITU-T H.264.
/// </summary>
public sealed class H264CodedBlockPatternReader
{
    // Table 9-4(a): ChromaArrayType 1 or 2.
    private static readonly byte[] Intra420422 =
    [
        47,31,15,0,23,27,29,30,7,11,13,14,
        39,43,45,46,16,3,5,10,12,19,21,26,
        28,35,37,42,44,1,2,4,8,17,18,20,
        24,6,9,22,25,32,33,34,36,40,38,41
    ];

    // Table 9-4(b): ChromaArrayType 0 or 3.
    private static readonly byte[] IntraMonochrome444 =
    [
        15,0,7,11,13,14,3,5,10,12,1,2,4,8,6,9
    ];

    /// <summary>Reads codeNum and maps it to CodedBlockPatternLuma/Chroma.</summary>
    public H264CodedBlockPattern ReadIntra(
        H264BitReader reader,
        uint chromaArrayType)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (chromaArrayType > 3)
            throw new ArgumentOutOfRangeException(nameof(chromaArrayType));

        uint codeNum = reader.ReadUnsignedExpGolomb();
        byte[] table = chromaArrayType is 1 or 2
            ? Intra420422
            : IntraMonochrome444;

        if (codeNum >= table.Length)
            throw new FormatException(
                $"coded_block_pattern codeNum {codeNum} is invalid for ChromaArrayType {chromaArrayType}.");

        uint codedBlockPattern = table[codeNum];
        uint chromaMask = chromaArrayType == 0 ? 0u : 3u;

        return new H264CodedBlockPattern(
            codeNum,
            codedBlockPattern & 15u,
            (codedBlockPattern >> 4) & chromaMask,
            chromaArrayType);
    }

    /// <summary>Maps an already decoded intra codeNum without consuming bits.</summary>
    public static H264CodedBlockPattern MapIntra(
        uint codeNum,
        uint chromaArrayType)
    {
        if (chromaArrayType > 3)
            throw new ArgumentOutOfRangeException(nameof(chromaArrayType));

        byte[] table = chromaArrayType is 1 or 2
            ? Intra420422
            : IntraMonochrome444;

        if (codeNum >= table.Length)
            throw new FormatException(
                $"coded_block_pattern codeNum {codeNum} is invalid for ChromaArrayType {chromaArrayType}.");

        uint codedBlockPattern = table[codeNum];
        uint chromaMask = chromaArrayType == 0 ? 0u : 3u;

        return new H264CodedBlockPattern(
            codeNum,
            codedBlockPattern & 15u,
            (codedBlockPattern >> 4) & chromaMask,
            chromaArrayType);
    }
}

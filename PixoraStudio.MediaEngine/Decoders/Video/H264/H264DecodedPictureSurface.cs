using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Owns a reconstructed 8-bit 4:2:0 or 4:2:2 frame and macroblock availability
/// metadata used by intra prediction. Coordinates are luma-sample coordinates
/// unless a method explicitly says macroblock.
/// </summary>
public sealed class H264DecodedPictureSurface
{
    private readonly byte[] _luma;
    private readonly byte[] _cb;
    private readonly byte[] _cr;
    private readonly int[] _sliceIds;
    private readonly bool[] _decoded;
    private readonly bool[] _intra;

    public H264DecodedPictureSurface(H264SequenceParameterSet sps)
    {
        ArgumentNullException.ThrowIfNull(sps);
        if (!sps.FrameMbsOnlyFlag)
            throw new NotSupportedException("Decoded picture surfaces currently support frame-only pictures.");
        if (sps.SeparateColourPlaneFlag || sps.ChromaFormatIdc is not (1 or 2))
            throw new NotSupportedException("Decoded picture surfaces currently support non-separate 4:2:0 and 4:2:2 pictures.");
        if (sps.BitDepthLumaMinus8 != 0 || sps.BitDepthChromaMinus8 != 0)
            throw new NotSupportedException("Decoded picture surfaces currently support 8-bit samples only.");

        MacroblockWidth = checked((int)sps.PicWidthInMbsMinus1 + 1);
        MacroblockHeight = checked((int)sps.PicHeightInMapUnitsMinus1 + 1);
        Width = checked(MacroblockWidth * 16);
        Height = checked(MacroblockHeight * 16);
        ChromaFormatIdc = sps.ChromaFormatIdc;
        ChromaWidth = Width / 2;
        ChromaHeight = ChromaFormatIdc == 1 ? Height / 2 : Height;
        _luma = new byte[checked(Width * Height)];
        _cb = new byte[checked(ChromaWidth * ChromaHeight)];
        _cr = new byte[checked(ChromaWidth * ChromaHeight)];
        int macroblockCount = checked(MacroblockWidth * MacroblockHeight);
        _sliceIds = new int[macroblockCount];
        Array.Fill(_sliceIds, -1);
        _decoded = new bool[macroblockCount];
        _intra = new bool[macroblockCount];
    }

    public int Width { get; }
    public int Height { get; }
    public int ChromaWidth { get; }
    public int ChromaHeight { get; }
    public int MacroblockWidth { get; }
    public int MacroblockHeight { get; }
    public uint ChromaFormatIdc { get; }

    public byte[] CopyLumaPlane() => (byte[])_luma.Clone();
    public byte[] CopyCbPlane() => (byte[])_cb.Clone();
    public byte[] CopyCrPlane() => (byte[])_cr.Clone();

    public byte GetLumaSample(int x, int y)
    {
        ValidateSampleCoordinate(x, y, Width, Height, nameof(x));
        return _luma[y * Width + x];
    }

    public byte GetChromaSample(byte componentId, int x, int y)
    {
        ValidateComponent(componentId);
        ValidateSampleCoordinate(x, y, ChromaWidth, ChromaHeight, nameof(x));
        return (componentId == 1 ? _cb : _cr)[y * ChromaWidth + x];
    }

    public bool IsMacroblockDecoded(int macroblockX, int macroblockY)
    {
        ValidateMacroblock(macroblockX, macroblockY);
        return _decoded[GetMacroblockIndex(macroblockX, macroblockY)];
    }

    public bool IsMacroblockAvailable(
        int macroblockX,
        int macroblockY,
        int currentSliceId,
        bool constrainedIntraPrediction)
    {
        ValidateMacroblock(macroblockX, macroblockY);
        if (currentSliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(currentSliceId));
        int index = GetMacroblockIndex(macroblockX, macroblockY);
        return _decoded[index] &&
            _sliceIds[index] == currentSliceId &&
            (!constrainedIntraPrediction || _intra[index]);
    }

    /// <summary>
    /// Stores one completely reconstructed macroblock. The decoded/availability
    /// marker is published only after all three planes have been copied.
    /// </summary>
    public void WriteIntraMacroblock(
        int macroblockX,
        int macroblockY,
        int sliceId,
        IReadOnlyList<byte> luma,
        IReadOnlyList<byte> cb,
        IReadOnlyList<byte> cr)
    {
        ValidateMacroblock(macroblockX, macroblockY);
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        ArgumentNullException.ThrowIfNull(luma);
        ArgumentNullException.ThrowIfNull(cb);
        ArgumentNullException.ThrowIfNull(cr);
        int chromaSamples = 8 * (ChromaFormatIdc == 1 ? 8 : 16);
        if (luma.Count != 256 || cb.Count != chromaSamples || cr.Count != chromaSamples)
            throw new ArgumentException("Macroblock plane buffers do not match the active chroma format.");

        int lumaX = macroblockX * 16;
        int lumaY = macroblockY * 16;
        CopyBlock(luma, _luma, Width, lumaX, lumaY, 16, 16);
        int chromaX = macroblockX * 8;
        int chromaY = macroblockY * (ChromaFormatIdc == 1 ? 8 : 16);
        int chromaBlockHeight = ChromaFormatIdc == 1 ? 8 : 16;
        CopyBlock(cb, _cb, ChromaWidth, chromaX, chromaY, 8, chromaBlockHeight);
        CopyBlock(cr, _cr, ChromaWidth, chromaX, chromaY, 8, chromaBlockHeight);

        int index = GetMacroblockIndex(macroblockX, macroblockY);
        _sliceIds[index] = sliceId;
        _intra[index] = true;
        _decoded[index] = true;
    }

    private static void CopyBlock(
        IReadOnlyList<byte> source, byte[] destination, int stride,
        int x, int y, int width, int height)
    {
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
                destination[(y + row) * stride + x + column] = source[row * width + column];
    }

    private int GetMacroblockIndex(int x, int y) => y * MacroblockWidth + x;

    private void ValidateMacroblock(int x, int y)
    {
        if ((uint)x >= (uint)MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(y));
    }

    private static void ValidateSampleCoordinate(int x, int y, int width, int height, string parameterName)
    {
        if ((uint)x >= (uint)width)
            throw new ArgumentOutOfRangeException(parameterName);
        if ((uint)y >= (uint)height)
            throw new ArgumentOutOfRangeException(nameof(y));
    }

    private static void ValidateComponent(byte componentId)
    {
        if (componentId is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(componentId));
    }
}

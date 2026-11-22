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
    private readonly bool[] _decoded
    private readonly bool[] _intra;
    private readonly H264DeblockingMacroblockMetadata?[] _deblockingMetadata;
    private bool _deblockingFilterApplied;

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
        _deblockingMetadata = new H264DeblockingMacroblockMetadata?[macroblockCount];
    }

    public int Width { get; }
    public int Height { get; }
    public int ChromaWidth { get; }
    public int ChromaHeight { get; }
    public int MacroblockWidth { get; }
    public int MacroblockHeight { get; }
    public uint ChromaFormatIdc { get; }
    public bool IsComplete => _decoded.All(decoded => decoded);
    public bool HasCompleteDeblockingMetadata =>
        _deblockingMetadata.All(metadata => metadata is not null);
    public bool IsDeblockingFilterApplied => _deblockingFilterApplied;

    public void SetDeblockingMetadata(
        int macroblockX,
        int macroblockY,
        H264DeblockingMacroblockMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (_deblockingFilterApplied)
            throw new InvalidOperationException("Deblocking metadata cannot change after picture filtering.");
        ValidateMacroblock(macroblockX, macroblockY);
        int index = GetMacroblockIndex(macroblockX, macroblockY);
        if (!_decoded[index])
            throw new InvalidOperationException("Deblocking metadata can only be attached after macroblock reconstruction.");
        metadata.Validate(checked((int)ChromaFormatIdc));
        _deblockingMetadata[index] = metadata.DeepCopy();
    }

    public H264DeblockingMacroblockMetadata? GetDeblockingMetadata(
        int macroblockX,
        int macroblockY)
    {
        ValidateMacroblock(macroblockX, macroblockY);
        return _deblockingMetadata[GetMacroblockIndex(macroblockX, macroblockY)]?.DeepCopy();
    }

    internal H264DeblockingMacroblockMetadata GetDeblockingMetadataForFiltering(
        int macroblockX,
        int macroblockY)
    {
        ValidateMacroblock(macroblockX, macroblockY);
        return _deblockingMetadata[GetMacroblockIndex(macroblockX, macroblockY)] ??
            throw new InvalidOperationException("Deblocking metadata is missing for the requested macroblock.");
    }

    internal Span<byte> GetLumaPlaneSpan() => _luma;
    internal int GetMacroblockSliceId(int x, int y)
    {
        ValidateMacroblock(x, y);
        return _sliceIds[GetMacroblockIndex(x, y)];
    }

    internal void MarkDeblockingFilterApplied()
    {
        if (_deblockingFilterApplied)
            throw new InvalidOperationException("The deblocking filter has already been applied to this picture.");
        _deblockingFilterApplied = true;
    }

    internal Span<byte> GetChromaPlaneSpan(byte componentId) => componentId switch
    {
        1 => _cb,
        2 => _cr,
        _ => throw new ArgumentOutOfRangeException(nameof(componentId))
    };

    public byte[] CopyLumaPlane() => (byte[])_luma.Clone();
    public byte[] CopyCbPlane() => (byte[])_cb.Clone();
    public byte[] CopyCrPlane() => (byte[])_cr.Clone();

    // Presentation conversion runs inside this assembly and only reads a
    // completed picture. Avoid cloning three full planes for every frame.
    internal ReadOnlySpan<byte> GetLumaPlaneReadOnlySpan() => _luma;
    internal ReadOnlySpan<byte> GetCbPlaneReadOnlySpan() => _cb;
    internal ReadOnlySpan<byte> GetCrPlaneReadOnlySpan() => _cr;

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

    /// <summary>
    /// Returns undecoded macroblock addresses in raster-scan order. This is
    /// intended for decoder diagnostics when a slice set leaves picture gaps.
    /// </summary>
    public IReadOnlyList<uint> GetUndecodedMacroblockAddresses()
    {
        List<uint> addresses = [];
        for (int index = 0; index < _decoded.Length; index++)
        {
            if (!_decoded[index])
                addresses.Add(checked((uint)index));
        }

        return addresses;
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
        IReadOnlyList<byte> cr) =>
        WriteMacroblock(macroblockX, macroblockY, sliceId, true, luma, cb, cr);

    /// <summary>
    /// Stores a decoded macroblock and records whether it was intra-coded.
    /// Cross-slice duplicates are rejected. Same-slice
    /// rewrites remain supported and invalidate previously attached metadata.
    /// This distinction is required by constrained_intra_pred_flag when
    /// reference samples are gathered for later macroblocks.
    /// </summary>
    public void WriteMacroblock(
        int macroblockX,
        int macroblockY,
        int sliceId,
        bool isIntra,
        IReadOnlyList<byte> luma,
        IReadOnlyList<byte> cb,
        IReadOnlyList<byte> cr)
    {
        if (_deblockingFilterApplied)
            throw new InvalidOperationException("A decoded picture cannot be modified after deblocking.");
        ValidateMacroblock(macroblockX, macroblockY);
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        ArgumentNullException.ThrowIfNull(luma);
        ArgumentNullException.ThrowIfNull(cb);
        ArgumentNullException.ThrowIfNull(cr);
        int chromaSamples = 8 * (ChromaFormatIdc == 1 ? 8 : 16);
        if (luma.Count != 256 || cb.Count != chromaSamples || cr.Count != chromaSamples)
            throw new ArgumentException("Macroblock plane buffers do not match the active chroma format.");

        int index = GetMacroblockIndex(macroblockX, macroblockY);
        if (_decoded[index] && _sliceIds[index] != sliceId)
        {
            throw new InvalidOperationException(
                "A macroblock cannot be reconstructed by more than one slice.");
        }

        // A same-slice rewrite temporarily makes the macroblock unavailable.
        // Publish it again only after all three planes and metadata are updated.
        _decoded[index] = false;
        try
        {
            _deblockingMetadata[index] = null;
            int lumaX = macroblockX * 16;
            int lumaY = macroblockY * 16;
            CopyBlock(luma, _luma, Width, lumaX, lumaY, 16, 16);
            int chromaX = macroblockX * 8;
            int chromaY = macroblockY * (ChromaFormatIdc == 1 ? 8 : 16);
            int chromaBlockHeight = ChromaFormatIdc == 1 ? 8 : 16;
            CopyBlock(cb, _cb, ChromaWidth, chromaX, chromaY, 8, chromaBlockHeight);
            CopyBlock(cr, _cr, ChromaWidth, chromaX, chromaY, 8, chromaBlockHeight);

            _sliceIds[index] = sliceId;
            _intra[index] = isIntra;
            _decoded[index] = true;
        }
        catch
        {
            _decoded[index] = false;
            throw;
        }
    }

    private static void CopyBlock(
        IReadOnlyList<byte> source, byte[] destination, int stride,
        int x, int y, int width, int height)
    {
        if (source is byte[] sourceArray)
        {
            // Reconstructed macroblock planes are normally byte arrays.
            // Array.Copy uses the runtime's optimised bulk-copy path instead
            // of performing one bounds-checked interface read per sample.
            for (int row = 0; row < height; row++)
            {
                Array.Copy(
                    sourceArray,
                    row * width,
                    destination,
                    (y + row) * stride + x,
                    width);
            }
            return;
        }

        for (int row = 0; row < height; row++)
        {
            int sourceOffset = row * width;
            int destinationOffset = (y + row) * stride + x;
            for (int column = 0; column < width; column++)
                destination[destinationOffset + column] = source[sourceOffset + column];
        }
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

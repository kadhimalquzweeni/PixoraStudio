namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Gathers macroblock-edge references from already reconstructed picture
/// samples. Neighbours in another slice, not yet decoded, or inter-coded when
/// constrained-intra prediction is enabled are unavailable.
/// </summary>
public sealed class H264IntraReferenceSampleProvider
{
    private readonly H264DecodedPictureSurface _surface;

    public H264IntraReferenceSampleProvider(H264DecodedPictureSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
    }

    public H264IntraReferenceSamples GatherLuma16x16(
        int macroblockX, int macroblockY, int sliceId, bool constrainedIntraPrediction)
    {
        ValidateRequest(macroblockX, macroblockY, sliceId);
        int x = macroblockX * 16;
        int y = macroblockY * 16;
        bool hasTop = macroblockY > 0 && _surface.IsMacroblockAvailable(
            macroblockX, macroblockY - 1, sliceId, constrainedIntraPrediction);
        bool hasLeft = macroblockX > 0 && _surface.IsMacroblockAvailable(
            macroblockX - 1, macroblockY, sliceId, constrainedIntraPrediction);
        bool hasTopLeft = macroblockX > 0 && macroblockY > 0 &&
            _surface.IsMacroblockAvailable(macroblockX - 1, macroblockY - 1,
                sliceId, constrainedIntraPrediction);

        byte[] top = new byte[16];
        byte[] left = new byte[16];
        if (hasTop)
            for (int i = 0; i < 16; i++)
                top[i] = _surface.GetLumaSample(x + i, y - 1);
        if (hasLeft)
            for (int i = 0; i < 16; i++)
                left[i] = _surface.GetLumaSample(x - 1, y + i);

        byte topLeft = hasTopLeft
            ? _surface.GetLumaSample(x - 1, y - 1)
            : (byte)0;
        return new H264IntraReferenceSamples(top, left, topLeft,
            hasTop, hasLeft, hasTopLeft);
    }

    public H264IntraReferenceSamples GatherChroma(
        byte componentId, int macroblockX, int macroblockY,
        int sliceId, bool constrainedIntraPrediction)
    {
        if (componentId is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(componentId));
        ValidateRequest(macroblockX, macroblockY, sliceId);
        int height = _surface.ChromaFormatIdc == 1 ? 8 : 16;
        int x = macroblockX * 8;
        int y = macroblockY * height;
        bool hasTop = macroblockY > 0 && _surface.IsMacroblockAvailable(
            macroblockX, macroblockY - 1, sliceId, constrainedIntraPrediction);
        bool hasLeft = macroblockX > 0 && _surface.IsMacroblockAvailable(
            macroblockX - 1, macroblockY, sliceId, constrainedIntraPrediction);
        bool hasTopLeft = macroblockX > 0 && macroblockY > 0 &&
            _surface.IsMacroblockAvailable(macroblockX - 1, macroblockY - 1,
                sliceId, constrainedIntraPrediction);

        byte[] top = new byte[8];
        byte[] left = new byte[height];
        if (hasTop)
            for (int i = 0; i < 8; i++)
                top[i] = _surface.GetChromaSample(componentId, x + i, y - 1);
        if (hasLeft)
            for (int i = 0; i < height; i++)
                left[i] = _surface.GetChromaSample(componentId, x - 1, y + i);
        byte topLeft = hasTopLeft
            ? _surface.GetChromaSample(componentId, x - 1, y - 1)
            : (byte)0;
        return new H264IntraReferenceSamples(top, left, topLeft,
            hasTop, hasLeft, hasTopLeft);
    }

    /// <summary>
    /// Gathers the eight top-right samples used by the top-right Intra8x8
    /// block from the macroblock above-right, when that macroblock is available.
    /// </summary>
    public (byte[] Samples, bool Available) GatherLumaTopRight8(
        int macroblockX, int macroblockY, int sliceId, bool constrainedIntraPrediction)
    {
        ValidateRequest(macroblockX, macroblockY, sliceId);
        byte[] samples = new byte[8];
        int neighbourX = macroblockX + 1;
        int neighbourY = macroblockY - 1;
        if (neighbourX >= _surface.MacroblockWidth || neighbourY < 0 ||
            !_surface.IsMacroblockAvailable(
                neighbourX, neighbourY, sliceId, constrainedIntraPrediction))
            return (samples, false);

        int sampleX = neighbourX * 16;
        int sampleY = macroblockY * 16 - 1;
        for (int i = 0; i < 8; i++)
            samples[i] = _surface.GetLumaSample(sampleX + i, sampleY);
        return (samples, true);
    }

    private void ValidateRequest(int x, int y, int sliceId)
    {
        if (sliceId < 0)
            throw new ArgumentOutOfRangeException(nameof(sliceId));
        if ((uint)x >= (uint)_surface.MacroblockWidth)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)_surface.MacroblockHeight)
            throw new ArgumentOutOfRangeException(nameof(y));
    }
}

/// <summary>Top, left and top-left reference samples plus normative availability.</summary>
public sealed record H264IntraReferenceSamples(
    IReadOnlyList<byte> Top,
    IReadOnlyList<byte> Left,
    byte TopLeft,
    bool HasTop,
    bool HasLeft,
    bool HasTopLeft);

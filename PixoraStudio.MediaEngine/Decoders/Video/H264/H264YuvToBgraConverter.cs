using System.Collections.Concurrent;
using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Converts supported 8-bit 4:2:0 and 4:2:2 decoded surfaces to BGRA32 for
/// presentation. Matrix and range are taken from SPS VUI when available.
/// </summary>
public sealed class H264YuvToBgraConverter
{
    private sealed record ConversionLookup(
        double[] Luma,
        double[] CbGreen,
        double[] CbBlue,
        double[] CrRed,
        double[] CrGreen);

    private static readonly ConcurrentDictionary<(bool FullRange, uint Matrix), ConversionLookup>
        LookupCache = new();

    public H264BgraFrame Convert(
        H264DecodedPictureSurface surface,
        H264SequenceParameterSet sps)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sps);
        if (!surface.IsComplete)
            throw new InvalidOperationException("A picture must be complete before colour conversion.");
        if (surface.Width != checked(((int)sps.PicWidthInMbsMinus1 + 1) * 16) ||
            surface.Height != checked(((int)sps.PicHeightInMapUnitsMinus1 + 1) * 16) ||
            surface.ChromaFormatIdc != sps.ChromaFormatIdc)
            throw new ArgumentException("The SPS geometry does not match the decoded surface.", nameof(sps));

        ReadOnlySpan<byte> yPlane = surface.GetLumaPlaneReadOnlySpan();
        ReadOnlySpan<byte> cbPlane = surface.GetCbPlaneReadOnlySpan();
        ReadOnlySpan<byte> crPlane = surface.GetCrPlaneReadOnlySpan();
        int width = surface.Width;
        int height = surface.Height;
        int chromaWidth = surface.ChromaWidth;
        int chromaSubsampleY = surface.ChromaFormatIdc == 1 ? 2 : 1;
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];

        bool fullRange = sps.VuiParameters?.VideoFullRangeFlag == true;
        uint matrix = sps.VuiParameters?.MatrixCoefficients ?? 6;
        ConversionLookup lookup = LookupCache.GetOrAdd(
            (fullRange, matrix),
            static key => CreateLookup(key.FullRange, key.Matrix));

        for (int row = 0; row < height; row++)
        {
            int lumaRowOffset = row * width;
            int chromaRowOffset = row / chromaSubsampleY * chromaWidth;
            int outputRowOffset = row * stride;

            for (int column = 0; column < width; column++)
            {
                int chromaIndex = chromaRowOffset + column / 2;
                double luma = lookup.Luma[yPlane[lumaRowOffset + column]];
                byte cb = cbPlane[chromaIndex];
                byte cr = crPlane[chromaIndex];

                double red = luma + lookup.CrRed[cr];
                double green = luma + lookup.CbGreen[cb] + lookup.CrGreen[cr];
                double blue = luma + lookup.CbBlue[cb];

                int outputIndex = outputRowOffset + column * 4;
                pixels[outputIndex] = Clip(blue);
                pixels[outputIndex + 1] = Clip(green);
                pixels[outputIndex + 2] = Clip(red);
                pixels[outputIndex + 3] = 255;
            }
        }

        return new H264BgraFrame(width, height, stride, pixels);
    }

    private static ConversionLookup CreateLookup(bool fullRange, uint matrix)
    {
        (double redCr, double greenCb, double greenCr, double blueCb) =
            GetMatrixCoefficients(matrix);

        double yScale = fullRange ? 1.0 : 255.0 / 219.0;
        double chromaScale = fullRange ? 1.0 : 255.0 / 224.0;
        double yOffset = fullRange ? 0.0 : 16.0;

        double[] luma = new double[256];
        double[] cbGreen = new double[256];
        double[] cbBlue = new double[256];
        double[] crRed = new double[256];
        double[] crGreen = new double[256];

        for (int value = 0; value < 256; value++)
        {
            luma[value] = (value - yOffset) * yScale;
            double chroma = (value - 128.0) * chromaScale;
            cbGreen[value] = greenCb * chroma;
            cbBlue[value] = blueCb * chroma;
            crRed[value] = redCr * chroma;
            crGreen[value] = greenCr * chroma;
        }

        return new ConversionLookup(luma, cbGreen, cbBlue, crRed, crGreen);
    }

    private static (double RedCr, double GreenCb, double GreenCr, double BlueCb)
        GetMatrixCoefficients(uint matrix)
    {
        if (matrix == 1)
            return (1.5748, -0.187324, -0.468124, 1.8556);

        if (matrix == 9 || matrix == 10)
            return (1.4746, -0.164553, -0.571353, 1.8814);

        return (1.402, -0.344136, -0.714136, 1.772);
    }

    private static byte Clip(double value) =>
        (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}

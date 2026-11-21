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

    /// <summary>Largest number of source samples averaged along one axis per output pixel.</summary>
    private const int MaximumSamplesPerAxis = 3;

    /// <summary>
    /// Converts a decoded picture straight to a smaller BGRA image. Each output
    /// pixel averages up to 3x3 source samples spread across the source area it
    /// covers, so the full-resolution BGRA frame (about 8 MB at 1080p) is never
    /// built. Scaling before colour conversion means far fewer pixels go through
    /// the colour matrix. The output keeps the source aspect ratio. A target width at or
    /// above the source width returns the full-size conversion unchanged.
    /// </summary>
    public H264BgraFrame ConvertScaled(
        H264DecodedPictureSurface surface,
        H264SequenceParameterSet sps,
        int targetWidth)
    {
        ValidateSurface(surface, sps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetWidth);

        int width = surface.Width;
        int height = surface.Height;
        if (targetWidth >= width)
            return Convert(surface, sps);

        int targetHeight = Math.Max(
            1, (int)Math.Round((double)height * targetWidth / width));

        ReadOnlySpan<byte> yPlane = surface.GetLumaPlaneReadOnlySpan();
        ReadOnlySpan<byte> cbPlane = surface.GetCbPlaneReadOnlySpan();
        ReadOnlySpan<byte> crPlane = surface.GetCrPlaneReadOnlySpan();
        int chromaWidth = surface.ChromaWidth;
        int chromaSubsampleY = surface.ChromaFormatIdc == 1 ? 2 : 1;
        ConversionLookup lookup = GetLookup(sps);

        int[] columns = CreateSamplePositions(width, targetWidth, out int samplesX);
        int[] rows = CreateSamplePositions(height, targetHeight, out int samplesY);
        double sampleWeight = 1.0 / (samplesX * samplesY);

        int stride = checked(targetWidth * 4);
        byte[] pixels = new byte[checked(stride * targetHeight)];

        for (int outputRow = 0; outputRow < targetHeight; outputRow++)
        {
            int outputRowOffset = outputRow * stride;

            for (int outputColumn = 0; outputColumn < targetWidth; outputColumn++)
            {
                double red = 0;
                double green = 0;
                double blue = 0;

                for (int sampleRow = 0; sampleRow < samplesY; sampleRow++)
                {
                    int sourceRow = rows[outputRow * samplesY + sampleRow];
                    int lumaRowOffset = sourceRow * width;
                    int chromaRowOffset = sourceRow / chromaSubsampleY * chromaWidth;

                    for (int sampleColumn = 0; sampleColumn < samplesX; sampleColumn++)
                    {
                        int sourceColumn = columns[outputColumn * samplesX + sampleColumn];
                        int chromaIndex = chromaRowOffset + sourceColumn / 2;
                        double luma = lookup.Luma[yPlane[lumaRowOffset + sourceColumn]];
                        byte cb = cbPlane[chromaIndex];
                        byte cr = crPlane[chromaIndex];

                        red += luma + lookup.CrRed[cr];
                        green += luma + lookup.CbGreen[cb] + lookup.CrGreen[cr];
                        blue += luma + lookup.CbBlue[cb];
                    }
                }

                int outputIndex = outputRowOffset + outputColumn * 4;
                pixels[outputIndex] = Clip(blue * sampleWeight);
                pixels[outputIndex + 1] = Clip(green * sampleWeight);
                pixels[outputIndex + 2] = Clip(red * sampleWeight);
                pixels[outputIndex + 3] = 255;
            }
        }

        return new H264BgraFrame(targetWidth, targetHeight, stride, pixels);
    }

    /// <summary>
    /// For each output index, picks <c>samples</c> source positions at the
    /// centres of equal sub-blocks of the source span that output index covers.
    /// The result is laid out as <c>[outputIndex * samples + sampleIndex]</c>.
    /// </summary>
    private static int[] CreateSamplePositions(
        int sourceLength,
        int targetLength,
        out int samplesPerOutput)
    {
        // Every output pixel covers at least floor(source / target) >= 1 source
        // samples because the caller only scales down.
        samplesPerOutput = Math.Clamp(sourceLength / targetLength, 1, MaximumSamplesPerAxis);
        int[] positions = new int[checked(targetLength * samplesPerOutput)];

        for (int output = 0; output < targetLength; output++)
        {
            long spanStart = (long)output * sourceLength / targetLength;
            long spanEnd = Math.Max(
                spanStart + 1, (long)(output + 1) * sourceLength / targetLength);
            long spanLength = spanEnd - spanStart;

            for (int sample = 0; sample < samplesPerOutput; sample++)
            {
                long offset = (2L * sample + 1) * spanLength / (2L * samplesPerOutput);
                positions[output * samplesPerOutput + sample] =
                    (int)Math.Min(spanStart + offset, sourceLength - 1);
            }
        }

        return positions;
    }

    private static void ValidateSurface(
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
    }

    private static ConversionLookup GetLookup(H264SequenceParameterSet sps)
    {
        bool fullRange = sps.VuiParameters?.VideoFullRangeFlag == true;
        uint matrix = sps.VuiParameters?.MatrixCoefficients ?? 6;
        return LookupCache.GetOrAdd(
            (fullRange, matrix),
            static key => CreateLookup(key.FullRange, key.Matrix));
    }

    public H264BgraFrame Convert(
        H264DecodedPictureSurface surface,
        H264SequenceParameterSet sps)
    {
        ValidateSurface(surface, sps);

        ReadOnlySpan<byte> yPlane = surface.GetLumaPlaneReadOnlySpan();
        ReadOnlySpan<byte> cbPlane = surface.GetCbPlaneReadOnlySpan();
        ReadOnlySpan<byte> crPlane = surface.GetCrPlaneReadOnlySpan();
        int width = surface.Width;
        int height = surface.Height;
        int chromaWidth = surface.ChromaWidth;
        int chromaSubsampleY = surface.ChromaFormatIdc == 1 ? 2 : 1;
        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];

        ConversionLookup lookup = GetLookup(sps);

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

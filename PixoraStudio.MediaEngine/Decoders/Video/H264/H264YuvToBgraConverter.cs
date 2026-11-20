using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Converts supported 8-bit 4:2:0 and 4:2:2 decoded surfaces to BGRA32 for
/// presentation. Matrix and range are taken from SPS VUI when available.
/// </summary>
public sealed class H264YuvToBgraConverter
{
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
        (double redCr, double greenCb, double greenCr, double blueCb) =
            GetMatrixCoefficients(matrix);

        double yScale = fullRange ? 1.0 : 255.0 / 219.0;
        double chromaScale = fullRange ? 1.0 : 255.0 / 224.0;
        double yOffset = fullRange ? 0.0 : 16.0;

        for (int row = 0; row < height; row++)
        {
            int lumaRowOffset = row * width;
            int chromaRowOffset = row / chromaSubsampleY * chromaWidth;
            int outputRowOffset = row * stride;

            for (int column = 0; column < width; column++)
            {
                int chromaIndex = chromaRowOffset + column / 2;
                double luma = (yPlane[lumaRowOffset + column] - yOffset) * yScale;
                double cb = (cbPlane[chromaIndex] - 128.0) * chromaScale;
                double cr = (crPlane[chromaIndex] - 128.0) * chromaScale;

                double red = luma + redCr * cr;
                double green = luma + greenCb * cb + greenCr * cr;
                double blue = luma + blueCb * cb;

                int outputIndex = outputRowOffset + column * 4;
                pixels[outputIndex] = Clip(blue);
                pixels[outputIndex + 1] = Clip(green);
                pixels[outputIndex + 2] = Clip(red);
                pixels[outputIndex + 3] = 255;
            }
        }

        return new H264BgraFrame(width, height, stride, pixels);
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

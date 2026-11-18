using PixoraStudio.MediaEngine.Models.Video;

namespace PixoraStudio.MediaEngine.Decoders.Video.H264;

/// <summary>
/// Produces H.264 list-0 motion-compensated prediction blocks from an
/// already reconstructed 8-bit reference picture. Luma uses the normative
/// six-tap half-sample filter and quarter-sample averaging. Chroma uses
/// bilinear interpolation with the 4:2:0/4:2:2 subsampling factors.
/// </summary>
public sealed class H264MotionCompensator
{
    private static readonly int[] SixTap = [1, -5, 20, 20, -5, 1];

    public H264MotionCompensatedBlock Predict(
        H264DecodedPictureSurface reference,
        int destinationX,
        int destinationY,
        int width,
        int height,
        H264MotionVector motionVector)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ValidateRegion(reference, destinationX, destinationY, width, height);

        byte[] luma = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            luma[y * width + x] = InterpolateLuma(
                reference,
                checked((destinationX + x) * 4 + motionVector.Horizontal),
                checked((destinationY + y) * 4 + motionVector.Vertical));
        }

        int chromaWidth = width / 2;
        int chromaHeight = reference.ChromaFormatIdc == 1 ? height / 2 : height;
        int chromaSubsampleY = reference.ChromaFormatIdc == 1 ? 2 : 1;
        byte[] cb = new byte[checked(chromaWidth * chromaHeight)];
        byte[] cr = new byte[cb.Length];

        for (int y = 0; y < chromaHeight; y++)
        for (int x = 0; x < chromaWidth; x++)
        {
            int destinationChromaX = destinationX / 2 + x;
            int destinationChromaY = destinationY / chromaSubsampleY + y;
            int sourceX8 = checked(destinationChromaX * 8 + motionVector.Horizontal);
            int sourceY8 = checked(destinationChromaY * 4 * chromaSubsampleY +
                motionVector.Vertical) / chromaSubsampleY;

            int index = y * chromaWidth + x;
            cb[index] = InterpolateChroma(reference, 1, sourceX8, sourceY8);
            cr[index] = InterpolateChroma(reference, 2, sourceX8, sourceY8);
        }

        return new H264MotionCompensatedBlock(
            width, height, chromaWidth, chromaHeight, luma, cb, cr);
    }

    /// <summary>
    /// Samples one luma position in quarter-luma-sample coordinates.
    /// Coordinates outside the reference frame use edge replication.
    /// </summary>
    public byte InterpolateLuma(
        H264DecodedPictureSurface reference,
        int quarterSampleX,
        int quarterSampleY)
    {
        ArgumentNullException.ThrowIfNull(reference);
        int integerX = FloorDiv(quarterSampleX, 4);
        int integerY = FloorDiv(quarterSampleY, 4);
        int fractionX = Mod(quarterSampleX, 4);
        int fractionY = Mod(quarterSampleY, 4);

        if (fractionX == 0 && fractionY == 0)
            return GetLumaClamped(reference, integerX, integerY);

        if ((fractionX & 1) == 0 && (fractionY & 1) == 0)
            return GetHalfGridSample(reference,
                integerX, integerY, fractionX / 2, fractionY / 2);

        if ((fractionX & 1) != 0 && fractionY == 0)
        {
            int left = GetHalfGridSample(reference, integerX, integerY,
                (fractionX - 1) / 2, 0);
            int right = GetHalfGridSample(reference, integerX, integerY,
                (fractionX + 1) / 2, 0);
            return Average(left, right);
        }

        if (fractionX == 0 && (fractionY & 1) != 0)
        {
            int top = GetHalfGridSample(reference, integerX, integerY,
                0, (fractionY - 1) / 2);
            int bottom = GetHalfGridSample(reference, integerX, integerY,
                0, (fractionY + 1) / 2);
            return Average(top, bottom);
        }

        int upperLeft = GetHalfGridSample(reference, integerX, integerY,
            (fractionX - 1) / 2, (fractionY - 1) / 2);
        int lowerRight = GetHalfGridSample(reference, integerX, integerY,
            (fractionX + 1) / 2, (fractionY + 1) / 2);
        return Average(upperLeft, lowerRight);
    }

    /// <summary>
    /// Samples one chroma position. Coordinates are in eighth-chroma-sample
    /// units horizontally and vertically; for 4:2:2 the caller supplies
    /// vertical coordinates scaled to the same eighth-sample denominator.
    /// </summary>
    public byte InterpolateChroma(
        H264DecodedPictureSurface reference,
        byte componentId,
        int eighthSampleX,
        int eighthSampleY)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (componentId is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(componentId));

        int x = FloorDiv(eighthSampleX, 8);
        int y = FloorDiv(eighthSampleY, 8);
        int dx = Mod(eighthSampleX, 8);
        int dy = Mod(eighthSampleY, 8);

        int a = GetChromaClamped(reference, componentId, x, y);
        if (dx == 0 && dy == 0)
            return (byte)a;

        int b = GetChromaClamped(reference, componentId, x + 1, y);
        int c = GetChromaClamped(reference, componentId, x, y + 1);
        int d = GetChromaClamped(reference, componentId, x + 1, y + 1);

        int weighted =
            (8 - dx) * (8 - dy) * a +
            dx * (8 - dy) * b +
            (8 - dx) * dy * c +
            dx * dy * d;
        return (byte)((weighted + 32) >> 6);
    }

    private static byte GetHalfGridSample(
        H264DecodedPictureSurface reference,
        int integerX,
        int integerY,
        int halfX,
        int halfY)
    {
        if (halfX == 0 && halfY == 0)
            return GetLumaClamped(reference, integerX, integerY);
        if (halfX == 1 && halfY == 0)
            return FilterHorizontal(reference, integerX, integerY);
        if (halfX == 0 && halfY == 1)
            return FilterVertical(reference, integerX, integerY);
        if (halfX == 1 && halfY == 1)
            return FilterDiagonal(reference, integerX, integerY);
        throw new ArgumentOutOfRangeException(nameof(halfX));
    }

    private static byte FilterHorizontal(
        H264DecodedPictureSurface reference, int x, int y)
    {
        int sum = 0;
        for (int tap = 0; tap < 6; tap++)
            sum += SixTap[tap] * GetLumaClamped(reference, x + tap - 2, y);
        return Clip((sum + 16) >> 5);
    }

    private static byte FilterVertical(
        H264DecodedPictureSurface reference, int x, int y)
    {
        int sum = 0;
        for (int tap = 0; tap < 6; tap++)
            sum += SixTap[tap] * GetLumaClamped(reference, x, y + tap - 2);
        return Clip((sum + 16) >> 5);
    }

    private static byte FilterDiagonal(
        H264DecodedPictureSurface reference, int x, int y)
    {
        int sum = 0;
        for (int verticalTap = 0; verticalTap < 6; verticalTap++)
        {
            int horizontalSum = 0;
            for (int horizontalTap = 0; horizontalTap < 6; horizontalTap++)
            {
                horizontalSum += SixTap[horizontalTap] *
                    GetLumaClamped(reference,
                        x + horizontalTap - 2,
                        y + verticalTap - 2);
            }
            sum += SixTap[verticalTap] * horizontalSum;
        }
        return Clip((sum + 512) >> 10);
    }

    private static byte GetLumaClamped(
        H264DecodedPictureSurface reference, int x, int y) =>
        reference.GetLumaSample(
            Math.Clamp(x, 0, reference.Width - 1),
            Math.Clamp(y, 0, reference.Height - 1));

    private static byte GetChromaClamped(
        H264DecodedPictureSurface reference, byte componentId, int x, int y) =>
        reference.GetChromaSample(
            componentId,
            Math.Clamp(x, 0, reference.ChromaWidth - 1),
            Math.Clamp(y, 0, reference.ChromaHeight - 1));

    private static byte Average(int first, int second) =>
        (byte)((first + second + 1) >> 1);

    private static byte Clip(int value) => (byte)Math.Clamp(value, 0, 255);

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static int Mod(int value, int divisor)
    {
        int remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }

    private static void ValidateRegion(
        H264DecodedPictureSurface reference,
        int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
            (x & 1) != 0 || (y & 1) != 0 ||
            (width & 1) != 0 || (height & 1) != 0 ||
            x > reference.Width - width || y > reference.Height - height)
            throw new ArgumentOutOfRangeException(nameof(width),
                "The luma partition must be an even-sized region inside the destination picture.");
    }
}

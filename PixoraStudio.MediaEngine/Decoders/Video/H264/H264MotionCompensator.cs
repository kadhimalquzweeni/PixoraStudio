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
        if (motionVector.Horizontal % 4 == 0 && motionVector.Vertical % 4 == 0)
        {
            CopyBlockWithEdgeReplication(
                reference.GetLumaPlaneReadOnlySpan(), reference.Width, reference.Height,
                checked(destinationX + motionVector.Horizontal / 4),
                checked(destinationY + motionVector.Vertical / 4),
                width, height, luma);
        }
        else
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                luma[y * width + x] = InterpolateLuma(
                    reference,
                    checked((destinationX + x) * 4 + motionVector.Horizontal),
                    checked((destinationY + y) * 4 + motionVector.Vertical));
            }
        }

        int chromaWidth = width / 2;
        int chromaHeight = reference.ChromaFormatIdc == 1 ? height / 2 : height;
        int chromaSubsampleY = reference.ChromaFormatIdc == 1 ? 2 : 1;
        byte[] cb = new byte[checked(chromaWidth * chromaHeight)];
        byte[] cr = new byte[cb.Length];
        int chromaVerticalDivisor = 4 * chromaSubsampleY;
        if (motionVector.Horizontal % 8 == 0 &&
            motionVector.Vertical % chromaVerticalDivisor == 0)
        {
            int sourceX = checked(destinationX / 2 + motionVector.Horizontal / 8);
            int sourceY = checked(destinationY / chromaSubsampleY +
                motionVector.Vertical / chromaVerticalDivisor);
            CopyBlockWithEdgeReplication(
                reference.GetCbPlaneReadOnlySpan(), reference.ChromaWidth,
                reference.ChromaHeight, sourceX, sourceY, chromaWidth, chromaHeight, cb);
            CopyBlockWithEdgeReplication(
                reference.GetCrPlaneReadOnlySpan(), reference.ChromaWidth,
                reference.ChromaHeight, sourceX, sourceY, chromaWidth, chromaHeight, cr);
        }
        else
        {
            for (int y = 0; y < chromaHeight; y++)
            for (int x = 0; x < chromaWidth; x++)
            {
                int destinationChromaX = destinationX / 2 + x;
                int destinationChromaY = destinationY / chromaSubsampleY + y;
                int sourceX8 = checked(destinationChromaX * 8 + motionVector.Horizontal);
                int sourceY8 = checked(destinationChromaY * 8 +
                    motionVector.Vertical * 2 / chromaSubsampleY);

                int index = y * chromaWidth + x;
                cb[index] = InterpolateChroma(reference, 1, sourceX8, sourceY8);
                cr[index] = InterpolateChroma(reference, 2, sourceX8, sourceY8);
            }
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
            return GetEvenGridSample(reference,
                integerX, integerY, fractionX, fractionY);

        if ((fractionX & 1) != 0 && fractionY == 0)
        {
            int left = GetEvenGridSample(reference, integerX, integerY,
                fractionX - 1, 0);
            int right = GetEvenGridSample(reference, integerX, integerY,
                fractionX + 1, 0);
            return Average(left, right);
        }

        if (fractionX == 0 && (fractionY & 1) != 0)
        {
            int top = GetEvenGridSample(reference, integerX, integerY,
                0, fractionY - 1);
            int bottom = GetEvenGridSample(reference, integerX, integerY,
                0, fractionY + 1);
            return Average(top, bottom);
        }

        int upperLeft = GetEvenGridSample(reference, integerX, integerY,
            fractionX - 1, fractionY - 1);
        int lowerRight = GetEvenGridSample(reference, integerX, integerY,
            fractionX + 1, fractionY + 1);
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

    private static byte GetEvenGridSample(
        H264DecodedPictureSurface reference,
        int integerX,
        int integerY,
        int quarterOffsetX,
        int quarterOffsetY)
    {
        int sampleX = integerX + quarterOffsetX / 4;
        int sampleY = integerY + quarterOffsetY / 4;
        int halfX = (quarterOffsetX % 4) / 2;
        int halfY = (quarterOffsetY % 4) / 2;
        return GetHalfGridSample(reference, sampleX, sampleY, halfX, halfY);
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

    private static void CopyBlockWithEdgeReplication(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        int sourceX,
        int sourceY,
        int blockWidth,
        int blockHeight,
        Span<byte> destination)
    {
        int expectedLength = checked(blockWidth * blockHeight);
        if (destination.Length != expectedLength)
            throw new ArgumentException(
                "The destination buffer does not match the requested block geometry.",
                nameof(destination));

        int leftPadding = sourceX < 0
            ? (int)Math.Min(blockWidth, -(long)sourceX)
            : 0;
        int firstSourceX = Math.Max(sourceX, 0);
        int copyWidth = Math.Min(
            Math.Max(0, sourceWidth - firstSourceX),
            blockWidth - leftPadding);
        int rightPadding = blockWidth - leftPadding - copyWidth;

        for (int row = 0; row < blockHeight; row++)
        {
            int sourceYClamped = (int)Math.Clamp(
                (long)sourceY + row, 0, sourceHeight - 1);
            int sourceRowOffset = sourceYClamped * sourceWidth;
            Span<byte> destinationRow = destination.Slice(row * blockWidth, blockWidth);

            if (leftPadding != 0)
                destinationRow[..leftPadding].Fill(source[sourceRowOffset]);

            if (copyWidth != 0)
            {
                source.Slice(sourceRowOffset + firstSourceX, copyWidth)
                    .CopyTo(destinationRow.Slice(leftPadding, copyWidth));
            }

            if (rightPadding != 0)
            {
                destinationRow.Slice(leftPadding + copyWidth, rightPadding)
                    .Fill(source[sourceRowOffset + sourceWidth - 1]);
            }
        }
    }

    private static byte GetLumaClamped(
        H264DecodedPictureSurface reference, int x, int y)
    {
        int clampedX = Math.Clamp(x, 0, reference.Width - 1);
        int clampedY = Math.Clamp(y, 0, reference.Height - 1);
        // This is an internal hot path: the frame dimensions and plane length
        // are established when the surface is created, and clamping guarantees
        // the resulting index is valid. Avoid the public accessor's repeated
        // argument validation for every six-tap filter sample.
        return reference.GetLumaPlaneReadOnlySpan()[clampedY * reference.Width + clampedX];
    }

    private static byte GetChromaClamped(
        H264DecodedPictureSurface reference, byte componentId, int x, int y)
    {
        int clampedX = Math.Clamp(x, 0, reference.ChromaWidth - 1);
        int clampedY = Math.Clamp(y, 0, reference.ChromaHeight - 1);
        ReadOnlySpan<byte> plane = componentId == 1
            ? reference.GetCbPlaneReadOnlySpan()
            : reference.GetCrPlaneReadOnlySpan();
        return plane[clampedY * reference.ChromaWidth + clampedX];
    }

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
            (x & 1) != 0 ||
            (reference.ChromaFormatIdc == 1 && (y & 1) != 0) ||
            (width & 1) != 0 || (height & 1) != 0 ||
            x > reference.Width - width || y > reference.Height - height)
            throw new ArgumentOutOfRangeException(nameof(width),
                "The luma partition must be an even-sized region inside the destination picture.");
    }
}

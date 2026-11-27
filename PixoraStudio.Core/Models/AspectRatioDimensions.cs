namespace PixoraStudio.Core.Models;

/// <summary>
/// Calculates integer output dimensions that preserve a source image's exact aspect ratio.
/// </summary>
public static class AspectRatioDimensions
{
    public static (int Width, int Height) FromWidth(
        int sourceWidth, int sourceHeight, int requestedWidth)
    {
        Validate(sourceWidth, sourceHeight, requestedWidth);
        int divisor = GreatestCommonDivisor(sourceWidth, sourceHeight);
        int widthUnit = sourceWidth / divisor;
        int heightUnit = sourceHeight / divisor;
        int multiplier = Math.Max(1, (int)Math.Round(
            (double)requestedWidth / widthUnit, MidpointRounding.AwayFromZero));
        return (checked(widthUnit * multiplier), checked(heightUnit * multiplier));
    }

    public static (int Width, int Height) FromHeight(
        int sourceWidth, int sourceHeight, int requestedHeight)
    {
        Validate(sourceWidth, sourceHeight, requestedHeight);
        int divisor = GreatestCommonDivisor(sourceWidth, sourceHeight);
        int widthUnit = sourceWidth / divisor;
        int heightUnit = sourceHeight / divisor;
        int multiplier = Math.Max(1, (int)Math.Round(
            (double)requestedHeight / heightUnit, MidpointRounding.AwayFromZero));
        return (checked(widthUnit * multiplier), checked(heightUnit * multiplier));
    }

    private static void Validate(int sourceWidth, int sourceHeight, int requestedDimension)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || requestedDimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedDimension),
                "Source and requested dimensions must be positive.");
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0)
            (left, right) = (right, left % right);
        return left;
    }
}

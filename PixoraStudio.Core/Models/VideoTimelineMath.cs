namespace PixoraStudio.Core.Models;

/// <summary>Converts between timeline coordinates and media positions.</summary>
public static class VideoTimelineMath
{
    public static double GetPosition(double x, double width, double durationMilliseconds)
    {
        if (!double.IsFinite(x) || !double.IsFinite(width) ||
            !double.IsFinite(durationMilliseconds) || width <= 0 ||
            durationMilliseconds <= 0)
            return 0;

        return Math.Clamp(x, 0, width) / width * durationMilliseconds;
    }

    public static double GetCoordinate(double positionMilliseconds, double width, double durationMilliseconds)
    {
        if (!double.IsFinite(positionMilliseconds) || !double.IsFinite(width) ||
            !double.IsFinite(durationMilliseconds) || width <= 0 ||
            durationMilliseconds <= 0)
            return 0;

        return Math.Clamp(positionMilliseconds, 0, durationMilliseconds) /
            durationMilliseconds * width;
    }

    public static double ClampPosition(double positionMilliseconds, double durationMilliseconds)
    {
        if (!double.IsFinite(positionMilliseconds) ||
            !double.IsFinite(durationMilliseconds) || durationMilliseconds <= 0)
            return 0;

        return Math.Clamp(positionMilliseconds, 0, durationMilliseconds);
    }
}

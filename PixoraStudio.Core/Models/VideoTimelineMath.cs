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

    /// <summary>
    /// Converts a click within a slider track into its value, accounting for
    /// the thumb width so the thumb centre lands at the clicked coordinate.
    /// </summary>
    public static double GetValueFromTrackClick(
        double clickX,
        double trackWidth,
        double thumbWidth,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(clickX) ||
            !double.IsFinite(trackWidth) ||
            !double.IsFinite(thumbWidth) ||
            !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            trackWidth <= 0 ||
            thumbWidth < 0 ||
            trackWidth <= thumbWidth ||
            maximum < minimum)
        {
            return minimum;
        }

        double usableWidth = trackWidth - thumbWidth;
        double thumbCentreOffset = thumbWidth / 2;
        double normalisedPosition = Math.Clamp(
            (clickX - thumbCentreOffset) / usableWidth,
            0,
            1);

        return minimum + normalisedPosition * (maximum - minimum);
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

    public static double ClampSliderValue(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) ||
            !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) ||
            maximum < minimum)
        {
            return minimum;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    public static double ClampPosition(double positionMilliseconds, double durationMilliseconds)
    {
        if (!double.IsFinite(positionMilliseconds) ||
            !double.IsFinite(durationMilliseconds) || durationMilliseconds <= 0)
            return 0;

        return Math.Clamp(positionMilliseconds, 0, durationMilliseconds);
    }
}

using SkiaSharp;

namespace PixoraStudio.Core.Geometry;

/// <summary>
/// Calculates transform handle positions and hit testing in document coordinates.
/// Handles are deliberately not clamped to the document canvas: layers may
/// extend beyond the canvas and must remain fully transformable.
/// </summary>
public static class LayerSelectionGeometry
{
    /// <summary>
    /// Returns handle centres in clockwise order: top-left, top, top-right,
    /// right, bottom-right, bottom, bottom-left, left.
    /// </summary>
    public static SKPoint[] GetHandlePoints(SKRect layerBounds)
    {
        ValidateBounds(layerBounds, nameof(layerBounds));

        var centreX = layerBounds.MidX;
        var centreY = layerBounds.MidY;
        return
        [
            new(layerBounds.Left, layerBounds.Top),
            new(centreX, layerBounds.Top),
            new(layerBounds.Right, layerBounds.Top),
            new(layerBounds.Right, centreY),
            new(layerBounds.Right, layerBounds.Bottom),
            new(centreX, layerBounds.Bottom),
            new(layerBounds.Left, layerBounds.Bottom),
            new(layerBounds.Left, centreY)
        ];
    }

    /// <summary>
    /// Returns the nearest handle index within the supplied hit radius,
    /// or null when the pointer is not over a handle.
    /// </summary>
    public static int? FindHandle(SKPoint point, SKRect layerBounds, float hitRadius)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
        if (hitRadius < 0 || !float.IsFinite(hitRadius))
            throw new ArgumentOutOfRangeException(nameof(hitRadius));

        var handles = GetHandlePoints(layerBounds);
        var maximumDistanceSquared = hitRadius * hitRadius;
        var bestIndex = -1;
        var bestDistanceSquared = float.PositiveInfinity;

        for (var i = 0; i < handles.Length; i++)
        {
            var dx = point.X - handles[i].X;
            var dy = point.Y - handles[i].Y;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared <= maximumDistanceSquared &&
                distanceSquared < bestDistanceSquared)
            {
                bestIndex = i;
                bestDistanceSquared = distanceSquared;
            }
        }

        return bestIndex < 0 ? null : bestIndex;
    }

    private static void ValidateBounds(SKRect bounds, string parameterName)
    {
        if (!float.IsFinite(bounds.Left) || !float.IsFinite(bounds.Top) ||
            !float.IsFinite(bounds.Right) || !float.IsFinite(bounds.Bottom) ||
            bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(parameterName,
                "Bounds must have finite coordinates and positive dimensions.");
    }
}

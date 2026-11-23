using SkiaSharp;

namespace PixoraStudio.Core.Geometry;

/// <summary>
/// Keeps a layer's transform handles reachable when its bounds extend beyond
/// the fixed document canvas. Layer coordinates remain unmodified; only the
/// on-screen handle centres are clamped to the visible canvas.
/// </summary>
public static class LayerSelectionGeometry
{
    /// <summary>
    /// Returns handle centres in clockwise order: top-left, top, top-right,
    /// right, bottom-right, bottom, bottom-left, left.
    /// </summary>
    public static SKPoint[] GetHandlePoints(SKRect layerBounds, SKRect canvasBounds)
    {
        ValidateBounds(layerBounds, nameof(layerBounds));
        ValidateBounds(canvasBounds, nameof(canvasBounds));

        var centreX = layerBounds.MidX;
        var centreY = layerBounds.MidY;
        SKPoint[] points =
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

        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new SKPoint(
                Math.Clamp(points[i].X, canvasBounds.Left, canvasBounds.Right),
                Math.Clamp(points[i].Y, canvasBounds.Top, canvasBounds.Bottom));
        }

        return points;
    }

    /// <summary>
    /// Returns the index of the nearest reachable handle within the supplied
    /// hit radius, or null when the pointer is not over a handle.
    /// </summary>
    public static int? FindHandle(
        SKPoint point, SKRect layerBounds, SKRect canvasBounds, float hitRadius)
    {
        if (hitRadius < 0 || !float.IsFinite(hitRadius))
            throw new ArgumentOutOfRangeException(nameof(hitRadius));

        var handles = GetHandlePoints(layerBounds, canvasBounds);
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

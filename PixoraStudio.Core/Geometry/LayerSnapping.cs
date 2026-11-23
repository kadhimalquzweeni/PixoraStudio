namespace PixoraStudio.Core.Geometry;

/// <summary>
/// Calculates alignment snapping for a moving image layer.
/// Coordinates are in document pixels.
/// </summary>
public static class LayerSnapping
{
    public static LayerSnapResult Snap(
        double x, double y, double width, double height,
        double canvasWidth, double canvasHeight,
        IEnumerable<LayerSnapBounds> otherLayers,
        double threshold)
    {
        ArgumentNullException.ThrowIfNull(otherLayers);
        if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));

        var xTargets = new List<double> { 0, canvasWidth / 2d, canvasWidth };
        var yTargets = new List<double> { 0, canvasHeight / 2d, canvasHeight };
        foreach (var layer in otherLayers)
        {
            xTargets.Add(layer.Left);
            xTargets.Add(layer.Left + layer.Width / 2d);
            xTargets.Add(layer.Right);
            yTargets.Add(layer.Top);
            yTargets.Add(layer.Top + layer.Height / 2d);
            yTargets.Add(layer.Bottom);
        }

        var xSnap = FindBestSnap([x, x + width / 2d, x + width], xTargets, threshold);
        var ySnap = FindBestSnap([y, y + height / 2d, y + height], yTargets, threshold);

        return new LayerSnapResult(
            x + (xSnap?.Delta ?? 0),
            y + (ySnap?.Delta ?? 0),
            xSnap?.Guide,
            ySnap?.Guide);
    }

    private static (double Delta, double Guide, double Distance)? FindBestSnap(
        IReadOnlyList<double> movingAnchors, IReadOnlyList<double> targets, double threshold)
    {
        (double Delta, double Guide, double Distance)? best = null;
        foreach (var anchor in movingAnchors)
        foreach (var target in targets)
        {
            var delta = target - anchor;
            var distance = Math.Abs(delta);
            if (distance > threshold || (best is not null && distance >= best.Value.Distance))
                continue;
            best = (delta, target, distance);
        }
        return best;
    }
}

public readonly record struct LayerSnapBounds(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct LayerSnapResult(double X, double Y, double? VerticalGuide, double? HorizontalGuide);

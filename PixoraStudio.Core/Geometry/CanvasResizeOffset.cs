namespace PixoraStudio.Core.Geometry;

/// <summary>Calculates how existing document content moves when the canvas changes size.</summary>
public readonly record struct CanvasResizeOffset(int X, int Y)
{
    public static CanvasResizeOffset Calculate(
        int oldWidth,
        int oldHeight,
        int newWidth,
        int newHeight,
        bool centreContent)
    {
        if (oldWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(oldWidth));
        if (oldHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(oldHeight));
        if (newWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(newWidth));
        if (newHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(newHeight));

        if (!centreContent)
            return new CanvasResizeOffset(0, 0);

        return new CanvasResizeOffset(
            (int)Math.Floor((newWidth - oldWidth) / 2d),
            (int)Math.Floor((newHeight - oldHeight) / 2d));
    }
}

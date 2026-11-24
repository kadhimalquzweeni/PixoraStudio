using SkiaSharp;

namespace PixoraStudio.Core.Geometry;

/// <summary>Pure crop-selection geometry shared by the editor and automated tests.</summary>
public static class CropSelectionGeometry
{
    public static SKRect ConstrainToAspectRatio(SKRect selection, SKRect bounds, float aspectRatio)
    {
        if (!float.IsFinite(aspectRatio) || aspectRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(aspectRatio), "Aspect ratio must be a finite positive number.");
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Crop bounds must have positive dimensions.");

        var clipped = new SKRect(
            Math.Clamp(selection.Left, bounds.Left, bounds.Right),
            Math.Clamp(selection.Top, bounds.Top, bounds.Bottom),
            Math.Clamp(selection.Right, bounds.Left, bounds.Right),
            Math.Clamp(selection.Bottom, bounds.Top, bounds.Bottom));

        if (clipped.Width <= 0 || clipped.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(selection), "Crop selection must overlap the image bounds.");

        // Keep the selection centre fixed while fitting the requested ratio inside
        // both the current selection and the image.
        float width = Math.Min(clipped.Width, clipped.Height * aspectRatio);
        width = Math.Min(width, Math.Min(bounds.Width, bounds.Height * aspectRatio));
        float height = width / aspectRatio;

        float centerX = (clipped.Left + clipped.Right) / 2f;
        float centerY = (clipped.Top + clipped.Bottom) / 2f;
        float left = Math.Clamp(centerX - width / 2f, bounds.Left, bounds.Right - width);
        float top = Math.Clamp(centerY - height / 2f, bounds.Top, bounds.Bottom - height);

        return new SKRect(left, top, left + width, top + height);
    }
}

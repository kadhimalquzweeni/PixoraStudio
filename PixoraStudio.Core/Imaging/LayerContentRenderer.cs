using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Core.Imaging;

/// <summary>Rasterises editable vector/text layer properties for shared CPU and GPU compositing.</summary>
public static class LayerContentRenderer
{
    public static SKBitmap Render(int width, int height, PhotoLayerContentType type,
        string text = "", string fontFamily = "Arial", float fontSize = 64,
        string fillColor = "#FFFFFFFF", string strokeColor = "#FF202020",
        float strokeWidth = 0, string shapeType = "Rectangle", string textAlignment = "Left")
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var fill = ParseColor(fillColor, SKColors.White);
        var stroke = ParseColor(strokeColor, SKColors.Black);

        if (type == PhotoLayerContentType.Text)
        {
            using var paint = new SKPaint { Color = fill, IsAntialias = true, TextSize = Math.Clamp(fontSize, 1f, 512f), Typeface = SKTypeface.FromFamilyName(fontFamily) };
            var metrics = paint.FontMetrics;
            var baseline = Math.Min(height - Math.Max(1, metrics.Descent), Math.Max(-metrics.Ascent, (height - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent));
            var content = text ?? string.Empty;
            float textWidth = paint.MeasureText(content);
            float textX = textAlignment == "Center" ? (width - textWidth) / 2f
                : textAlignment == "Right" ? width - textWidth - 4 : 4;
            canvas.DrawText(content, textX, baseline, paint);
        }
        else if (type is PhotoLayerContentType.Shape or PhotoLayerContentType.Frame)
        {
            var inset = Math.Max(0, strokeWidth) / 2f;
            var rect = new SKRect(inset, inset, Math.Max(inset, width - inset), Math.Max(inset, height - inset));
            bool frame = type == PhotoLayerContentType.Frame;
            if (shapeType == "Ellipse")
            {
                using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var strokePaint = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(frame ? 1 : 0, strokeWidth), IsAntialias = true };
                if (!frame) canvas.DrawOval(rect, fillPaint);
                if (frame || strokeWidth > 0) canvas.DrawOval(rect, strokePaint);
            }
            else if (shapeType is "Line" or "Arrow")
            {
                using var paint = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, strokeWidth), StrokeCap = SKStrokeCap.Round, IsAntialias = true };
                canvas.DrawLine(0, height / 2f, width, height / 2f, paint);
                if (shapeType == "Arrow")
                {
                    float head = Math.Min(24, Math.Min(width, height) / 3f);
                    canvas.DrawLine(width - head, height / 2f - head / 2f, width, height / 2f, paint);
                    canvas.DrawLine(width - head, height / 2f + head / 2f, width, height / 2f, paint);
                }
            }
            else
            {
                if (!frame)
                {
                    using var fillPaint = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
                    canvas.DrawRect(rect, fillPaint);
                }
                using var strokePaint = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(frame ? 1 : 0, strokeWidth), IsAntialias = true };
                if (frame || strokeWidth > 0) canvas.DrawRect(rect, strokePaint);
            }
        }
        canvas.Flush();
        return bitmap;
    }

    private static SKColor ParseColor(string? value, SKColor fallback)
    {
        try { return string.IsNullOrWhiteSpace(value) ? fallback : SKColor.Parse(value); }
        catch { return fallback; }
    }
}

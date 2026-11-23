using PixoraStudio.Core.Models;
using SkiaSharp;

namespace PixoraStudio.Core.Imaging;

/// <summary>A document layer supplied to the GPU preview renderer.</summary>
public readonly record struct GpuImageLayer(
    SKBitmap Bitmap,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsVisible,
    ImageAdjustment Adjustment);

/// <summary>
/// Renders and composites image layers through Skia's drawing pipeline. When
/// called with an OpenGL-backed SKCanvas, the bitmap sampling, colour-filter
/// shader and source-over compositing execute on the GPU. The same renderer can
/// run on a raster canvas for deterministic tests and CPU fallback scenarios.
/// </summary>
public static class GpuImageRenderer
{
    private const string AdjustmentShader = """
        uniform float brightness;
        uniform float contrast;
        uniform float exposure;
        uniform float highlights;
        uniform float shadows;
        uniform float temperature;
        uniform float tint;
        uniform int filterMode;

        half4 main(half4 inputColor)
        {
            float alpha = inputColor.a;
            if (alpha <= 0.0)
                return half4(0.0);

            // Skia's rendering pipeline uses premultiplied colours. Work in
            // straight RGB for photo adjustments, then premultiply the result.
            float3 rgb = (float3(inputColor.rgb) / alpha) * 255.0;
            float exposureMultiplier = pow(2.0, clamp(exposure, -8.0, 8.0));

            rgb = (rgb * exposureMultiplier) + brightness;
            rgb = ((rgb - 128.0) * contrast) + 128.0;

            float luminance = clamp(
                (0.2126 * rgb.r + 0.7152 * rgb.g + 0.0722 * rgb.b) / 255.0,
                0.0, 1.0);
            float highlightMask = luminance * luminance;
            float shadowMask = (1.0 - luminance) * (1.0 - luminance);
            float localTone = (highlights * highlightMask + shadows * shadowMask) * 110.0;
            rgb += localTone;

            float gray = dot(rgb, float3(0.299, 0.587, 0.114));
            rgb = gray + contrast * 0.0 + (rgb - gray) * 1.0;
            // Saturation is passed through a dedicated uniform to keep the
            // shader's operation order aligned with the CPU processing path.
            rgb = gray + saturation * (rgb - gray);

            rgb.r += temperature + tint;
            rgb.g -= tint * 2.0;
            rgb.b -= temperature - tint;

            if (filterMode == 1)
            {
                rgb = float3(gray);
            }
            else if (filterMode == 2)
            {
                float3 sepia = float3(
                    dot(rgb, float3(0.393, 0.769, 0.189)),
                    dot(rgb, float3(0.349, 0.686, 0.168)),
                    dot(rgb, float3(0.272, 0.534, 0.131)));
                rgb = sepia;
            }

            return half4(half3(clamp(rgb / 255.0, 0.0, 1.0) * alpha), half(alpha));
        }
        """;

    private static readonly Lazy<SKRuntimeEffect> AdjustmentEffect = new(() =>
    {
        var effect = SKRuntimeEffect.CreateColorFilter(AdjustmentShader, out var errors);
        return effect ?? throw new InvalidOperationException(
            $"Pixora's GPU adjustment shader could not be compiled: {errors}");
    });

    private static readonly SKSamplingOptions Sampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>
    /// Clears the document to transparent and draws visible layers in the
    /// supplied bottom-to-top order, clipping all content to the document.
    /// </summary>
    public static void Render(
        SKCanvas canvas,
        int documentWidth,
        int documentHeight,
        IReadOnlyCollection<GpuImageLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layers);
        if (documentWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(documentWidth));
        if (documentHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(documentHeight));

        int saveCount = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0, 0, documentWidth, documentHeight));
            canvas.Clear(SKColors.Transparent);

            foreach (var layer in layers)
            {
                ArgumentNullException.ThrowIfNull(layer.Bitmap);
                ArgumentNullException.ThrowIfNull(layer.Adjustment);
                if (!layer.IsVisible || layer.Width <= 0 || layer.Height <= 0)
                    continue;

                using var uniforms = new SKRuntimeEffectUniforms(AdjustmentEffect.Value);
                uniforms["brightness"] = layer.Adjustment.Brightness;
                uniforms["contrast"] = layer.Adjustment.Contrast;
                uniforms["saturation"] = layer.Adjustment.Saturation;
                uniforms["exposure"] = layer.Adjustment.Exposure;
                uniforms["highlights"] = Math.Clamp(layer.Adjustment.Highlights, -100f, 100f) / 100f;
                uniforms["shadows"] = Math.Clamp(layer.Adjustment.Shadows, -100f, 100f) / 100f;
                uniforms["temperature"] = Math.Clamp(layer.Adjustment.Temperature, -100f, 100f) * 0.65f;
                uniforms["tint"] = Math.Clamp(layer.Adjustment.Tint, -100f, 100f) * 0.45f;
                uniforms["filterMode"] = (int)layer.Adjustment.Filter;

                using var colorFilter = AdjustmentEffect.Value.ToColorFilter(uniforms);
                using var paint = new SKPaint
                {
                    ColorFilter = colorFilter,
                    IsAntialias = true
                };

                var destination = new SKRect(
                    layer.X, layer.Y,
                    layer.X + layer.Width, layer.Y + layer.Height);
                canvas.DrawBitmap(layer.Bitmap, destination, Sampling, paint);
            }

            canvas.Flush();
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }
}

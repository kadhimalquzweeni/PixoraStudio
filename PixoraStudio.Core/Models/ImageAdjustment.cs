
namespace PixoraStudio.Core.Models;

public class ImageAdjustment
{
    public float Brightness { get; set; } = 0f;

    public float Contrast { get; set; } = 1f;

    public float Saturation { get; set; } = 1f;

    public ImageFilter Filter { get; set; } = ImageFilter.None;
}

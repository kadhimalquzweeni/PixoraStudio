using SkiaSharp.Views.WPF;

namespace PixoraStudio.App.Controls;

/// <summary>
/// Wraps SKGLElement's context creation and paint loop so unsupported graphics
/// drivers can fall back to the editor's CPU preview instead of crashing the view.
/// </summary>
public sealed class SafeGpuPreviewElement : SKGLElement
{
    public event EventHandler? RenderFailed;

    protected override void OnPaint(TimeSpan e)
    {
        try
        {
            base.OnPaint(e);
        }
        catch (Exception)
        {
            RenderFailed?.Invoke(this, EventArgs.Empty);
        }
    }
}

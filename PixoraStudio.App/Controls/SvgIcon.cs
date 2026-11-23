using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace PixoraStudio.App.Controls;

/// <summary>
/// Renders the project's Lucide SVG assets using WPF drawing primitives.
/// </summary>
public sealed class SvgIcon : FrameworkElement
{
    public static readonly DependencyProperty IconNameProperty =
        DependencyProperty.Register(nameof(IconName), typeof(string), typeof(SvgIcon),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SvgIcon),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(SvgIcon),
            new FrameworkPropertyMetadata(2d, FrameworkPropertyMetadataOptions.AffectsRender));

    public string IconName
    {
        get => (string)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(24, 24);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (string.IsNullOrWhiteSpace(IconName))
            return;

        var uri = new Uri(
            $"pack://application:,,,/PixoraStudio.App;component/Assets/Icons/{IconName}.svg",
            UriKind.Absolute);

        try
        {
            var resource = Application.GetResourceStream(uri);
            if (resource is null)
                return;

            using (resource.Stream)
            {
                var document = XDocument.Load(resource.Stream);
            var root = document.Root;
            if (root is null)
                return;

            var viewBox = (string?)root.Attribute("viewBox") ?? "0 0 24 24";
            var viewBoxParts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (viewBoxParts.Length != 4 ||
                !double.TryParse(viewBoxParts[2], CultureInfo.InvariantCulture, out var viewWidth) ||
                !double.TryParse(viewBoxParts[3], CultureInfo.InvariantCulture, out var viewHeight) ||
                viewWidth <= 0 || viewHeight <= 0)
                return;

            var pen = new Pen(Stroke, StrokeThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            if (pen.CanFreeze) pen.Freeze();

            var group = new DrawingGroup();
            using (var context = group.Open())
            {
                foreach (var element in root.Elements())
                {
                    Geometry? geometry = element.Name.LocalName switch
                    {
                        "path" when (string?)element.Attribute("d") is { } data => Geometry.Parse(data),
                        "circle" => CreateCircle(element),
                        "rect" => CreateRectangle(element),
                        "line" => CreateLine(element),
                        _ => null
                    };

                    if (geometry is not null)
                        context.DrawGeometry(null, pen, geometry);
                }
            }

            var scaleX = ActualWidth / viewWidth;
            var scaleY = ActualHeight / viewHeight;
            drawingContext.PushTransform(new ScaleTransform(scaleX, scaleY));
            drawingContext.DrawDrawing(group);
                drawingContext.Pop();
            }
        }
        catch (IOException)
        {
            // A missing or unavailable icon must never take down the editor during rendering.
        }
        catch (System.Xml.XmlException)
        {
            // Ignore malformed SVG assets and keep the rest of the editor usable.
        }
    }

    private static Geometry CreateCircle(XElement element)
    {
        var cx = Read(element, "cx");
        var cy = Read(element, "cy");
        var radius = Read(element, "r");
        return new EllipseGeometry(new Point(cx, cy), radius, radius);
    }

    private static Geometry CreateRectangle(XElement element)
    {
        var x = Read(element, "x");
        var y = Read(element, "y");
        var width = Read(element, "width");
        var height = Read(element, "height");
        var radius = Read(element, "rx");
        return radius > 0
            ? new RectangleGeometry(new Rect(x, y, width, height), radius, radius)
            : new RectangleGeometry(new Rect(x, y, width, height));
    }

    private static Geometry CreateLine(XElement element) =>
        new LineGeometry(
            new Point(Read(element, "x1"), Read(element, "y1")),
            new Point(Read(element, "x2"), Read(element, "y2")));

    private static double Read(XElement element, string attribute) =>
        double.TryParse((string?)element.Attribute(attribute), CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
}

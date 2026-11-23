using PixoraStudio.App.ViewModels;
using PixoraStudio.Core.Models;
using PixoraStudio.Core.Geometry;
using SkiaSharp;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PixoraStudio.App.Views;

public partial class PhotoEditorView : UserControl
{
    private Rect _cropSelection;
    private Point _dragStart;
    private Rect _selectionAtDragStart;
    private DragMode _dragMode;
    private bool _isDragging;
    private const double MinimumCropSize = 8;
    private const double HandleRadius = 7;
    private const double LayerHandleRadius = 6;
    private const int MinimumLayerSize = 8;
    private Rect _layerSelection;
    private Point _layerDragStart;
    private Rect _layerBoundsAtDragStart;
    private DragMode _layerDragMode;
    private bool _isLayerDragging;

    private enum DragMode { None, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    public PhotoEditorView()
    {
        InitializeComponent();
    }

    private PhotoEditorViewModel? ViewModel => DataContext as PhotoEditorViewModel;

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !int.TryParse(tag, out int selectedTool) ||
            selectedTool < 0 || selectedTool >= ToolSettingsTabs.Items.Count)
            return;

        ToolSettingsTabs.SelectedIndex = selectedTool;
        ToolSettingsColumn.Width = new GridLength(280);
        ToolSettingsBorder.Visibility = Visibility.Visible;
    }

    private void CloseToolSettings_Click(object sender, RoutedEventArgs e)
    {
        ToolSettingsBorder.Visibility = Visibility.Collapsed;
        ToolSettingsColumn.Width = new GridLength(0);
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainViewModel)
            mainViewModel.NavigateHomeCommand.Execute(null);
    }

    private void StartCrop_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsImageLoaded: true } vm || vm.ImageWidth < 1 || vm.ImageHeight < 1)
            return;

        var insetX = Math.Max(1, vm.ImageWidth * 0.1);
        var insetY = Math.Max(1, vm.ImageHeight * 0.1);
        _cropSelection = new Rect(insetX, insetY,
            Math.Max(MinimumCropSize, vm.ImageWidth - insetX * 2),
            Math.Max(MinimumCropSize, vm.ImageHeight - insetY * 2));
        _cropSelection.Intersect(new Rect(0, 0, vm.ImageWidth, vm.ImageHeight));
        _cropSelection = ApplySelectedAspectRatio(_cropSelection, vm);
        CropOverlay.Visibility = Visibility.Visible;
        CropActionPanel.Visibility = Visibility.Visible;
        DrawCropOverlay();
        UpdateCropCoordinates();
    }

    private void ApplyVisualCrop_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsImageLoaded: true } vm) return;
        UpdateCropCoordinates();
        vm.CropCommand.Execute(null);
        EndCropMode();
    }

    private void CancelVisualCrop_Click(object sender, RoutedEventArgs e) => EndCropMode();

    private void CropAspectRatio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CropOverlay is null || CropOverlay.Visibility != Visibility.Visible ||
            ViewModel is not { IsImageLoaded: true } vm)
            return;

        _cropSelection = ApplySelectedAspectRatio(_cropSelection, vm);
        DrawCropOverlay();
        UpdateCropCoordinates();
    }

    private Rect ApplySelectedAspectRatio(Rect selection, PhotoEditorViewModel vm)
    {
        if (CropAspectRatioCombo.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag || tag == "free")
            return selection;

        float ratio = tag == "original"
            ? (float)vm.ImageWidth / Math.Max(1, vm.ImageHeight)
            : float.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);

        var adjusted = CropSelectionGeometry.ConstrainToAspectRatio(
            new SKRect((float)selection.Left, (float)selection.Top,
                       (float)selection.Right, (float)selection.Bottom),
            new SKRect(0, 0, vm.ImageWidth, vm.ImageHeight), ratio);

        return new Rect(adjusted.Left, adjusted.Top, adjusted.Width, adjusted.Height);
    }

    private void EndCropMode()
    {
        CropOverlay.Visibility = Visibility.Collapsed;
        CropActionPanel.Visibility = Visibility.Collapsed;
        CropOverlay.Children.Clear();
    }

    private void CropOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsImageLoaded: true }) return;
        var point = e.GetPosition(CropOverlay);
        _dragMode = GetDragMode(point);
        _dragStart = point;
        _selectionAtDragStart = _cropSelection;
        _isDragging = true;
        CropOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void CropOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || ViewModel is not { } vm) return;
        var point = e.GetPosition(CropOverlay);
        var dx = point.X - _dragStart.X;
        var dy = point.Y - _dragStart.Y;
        var bounds = new Rect(0, 0, vm.ImageWidth, vm.ImageHeight);
        var selection = _selectionAtDragStart;

        switch (_dragMode)
        {
            case DragMode.Move:
                selection.X = Math.Clamp(selection.X + dx, 0, bounds.Width - selection.Width);
                selection.Y = Math.Clamp(selection.Y + dy, 0, bounds.Height - selection.Height);
                break;
            case DragMode.Left:
            case DragMode.TopLeft:
            case DragMode.BottomLeft:
                selection = ResizeLeft(selection, dx);
                break;
        }
        if (_dragMode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight)
            selection.Width = Math.Clamp(_selectionAtDragStart.Width + dx, MinimumCropSize, bounds.Right - selection.Left);
        if (_dragMode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight)
            selection = ResizeTop(selection, dy);
        if (_dragMode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight)
            selection.Height = Math.Clamp(_selectionAtDragStart.Height + dy, MinimumCropSize, bounds.Bottom - selection.Top);

        _cropSelection = ApplySelectedAspectRatio(selection, vm);
        DrawCropOverlay();
        UpdateCropCoordinates();
    }

    private Rect ResizeLeft(Rect rect, double dx)
    {
        var right = rect.Right;
        var left = Math.Clamp(rect.Left + dx, 0, right - MinimumCropSize);
        rect.X = left;
        rect.Width = right - left;
        return rect;
    }

    private Rect ResizeTop(Rect rect, double dy)
    {
        var bottom = rect.Bottom;
        var top = Math.Clamp(rect.Top + dy, 0, bottom - MinimumCropSize);
        rect.Y = top;
        rect.Height = bottom - top;
        return rect;
    }

    private void CropOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        CropOverlay.ReleaseMouseCapture();
        e.Handled = true;
    }

    private DragMode GetDragMode(Point point)
    {
        var left = _cropSelection.Left;
        var right = _cropSelection.Right;
        var top = _cropSelection.Top;
        var bottom = _cropSelection.Bottom;
        var nearLeft = Math.Abs(point.X - left) <= HandleRadius * 2;
        var nearRight = Math.Abs(point.X - right) <= HandleRadius * 2;
        var nearTop = Math.Abs(point.Y - top) <= HandleRadius * 2;
        var nearBottom = Math.Abs(point.Y - bottom) <= HandleRadius * 2;

        if (nearLeft && nearTop) return DragMode.TopLeft;
        if (nearRight && nearTop) return DragMode.TopRight;
        if (nearLeft && nearBottom) return DragMode.BottomLeft;
        if (nearRight && nearBottom) return DragMode.BottomRight;
        if (nearLeft && point.Y >= top && point.Y <= bottom) return DragMode.Left;
        if (nearRight && point.Y >= top && point.Y <= bottom) return DragMode.Right;
        if (nearTop && point.X >= left && point.X <= right) return DragMode.Top;
        if (nearBottom && point.X >= left && point.X <= right) return DragMode.Bottom;
        return _cropSelection.Contains(point) ? DragMode.Move : DragMode.None;
    }

    private void DrawCropOverlay()
    {
        CropOverlay.Children.Clear();
        var width = CropOverlay.Width;
        var height = CropOverlay.Height;
        var left = _cropSelection.Left;
        var top = _cropSelection.Top;
        var right = _cropSelection.Right;
        var bottom = _cropSelection.Bottom;

        AddShade(0, 0, width, top);
        AddShade(0, bottom, width, height - bottom);
        AddShade(0, top, left, _cropSelection.Height);
        AddShade(right, top, width - right, _cropSelection.Height);

        var border = new Rectangle
        {
            Width = _cropSelection.Width,
            Height = _cropSelection.Height,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        CropOverlay.Children.Add(border);

        foreach (var x in new[] { left, _cropSelection.Left + _cropSelection.Width / 2, right })
        foreach (var y in new[] { top, _cropSelection.Top + _cropSelection.Height / 2, bottom })
        {
            if ((x == left || x == right) && (y == top || y == bottom))
                AddHandle(x, y);
        }
        AddHandle(_cropSelection.Left + _cropSelection.Width / 2, top);
        AddHandle(_cropSelection.Left + _cropSelection.Width / 2, bottom);
        AddHandle(left, _cropSelection.Top + _cropSelection.Height / 2);
        AddHandle(right, _cropSelection.Top + _cropSelection.Height / 2);
    }

    private void AddShade(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        var shade = new Rectangle
        {
            Width = width, Height = height,
            Fill = new SolidColorBrush(Color.FromArgb(145, 0, 0, 0)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(shade, x);
        Canvas.SetTop(shade, y);
        CropOverlay.Children.Add(shade);
    }

    private void AddHandle(double x, double y)
    {
        var handle = new Ellipse
        {
            Width = HandleRadius * 2, Height = HandleRadius * 2,
            Fill = Brushes.White, Stroke = Brushes.DodgerBlue, StrokeThickness = 2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(handle, x - HandleRadius);
        Canvas.SetTop(handle, y - HandleRadius);
        CropOverlay.Children.Add(handle);
    }

    private void UpdateCropCoordinates()
    {
        if (ViewModel is not { } vm) return;
        vm.CropX = Math.Max(0, (int)Math.Round(_cropSelection.Left));
        vm.CropY = Math.Max(0, (int)Math.Round(_cropSelection.Top));
        vm.CropWidth = Math.Max(1, (int)Math.Round(_cropSelection.Width));
        vm.CropHeight = Math.Max(1, (int)Math.Round(_cropSelection.Height));
    }


    private void LayerOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsImageLoaded: true } vm || CropOverlay.Visibility == Visibility.Visible) return;
        var point = e.GetPosition(LayerOverlay);
        var selected = vm.SelectedLayer;
        if (selected is not null && selected.IsVisible)
        {
            var current = new Rect(selected.X, selected.Y, selected.Width, selected.Height);
            _layerDragMode = GetLayerDragMode(point, current);
        }
        if (_layerDragMode == DragMode.None)
        {
            selected = vm.HitTestLayer(point.X, point.Y);
            vm.SelectLayer(selected);
            if (selected is null)
            {
                LayerOverlay.Children.Clear();
                return;
            }
            _layerDragMode = DragMode.Move;
        }
        _layerBoundsAtDragStart = new Rect(selected!.X, selected.Y, selected.Width, selected.Height);
        _layerSelection = _layerBoundsAtDragStart;
        _layerDragStart = point;
        _isLayerDragging = true;
        LayerOverlay.CaptureMouse();
        DrawLayerSelection();
        e.Handled = true;
    }

    private void LayerOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isLayerDragging || ViewModel?.SelectedLayer is not { } layer) return;
        var point = e.GetPosition(LayerOverlay);
        var dx = point.X - _layerDragStart.X;
        var dy = point.Y - _layerDragStart.Y;
        var bounds = _layerBoundsAtDragStart;
        var x = bounds.X;
        var y = bounds.Y;
        var width = bounds.Width;
        var height = bounds.Height;

        if (_layerDragMode == DragMode.Move)
        {
            x += dx;
            y += dy;
        }
        else
        {
            if (_layerDragMode is DragMode.Left or DragMode.TopLeft or DragMode.BottomLeft)
            {
                var right = bounds.Right;
                x = Math.Min(right - MinimumLayerSize, bounds.Left + dx);
                width = right - x;
            }
            if (_layerDragMode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight)
                width = Math.Max(MinimumLayerSize, bounds.Width + dx);
            if (_layerDragMode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight)
            {
                var bottom = bounds.Bottom;
                y = Math.Min(bottom - MinimumLayerSize, bounds.Top + dy);
                height = bottom - y;
            }
            if (_layerDragMode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight)
                height = Math.Max(MinimumLayerSize, bounds.Height + dy);

            if (_layerDragMode is DragMode.TopLeft or DragMode.TopRight or DragMode.BottomLeft or DragMode.BottomRight)
            {
                var ratio = bounds.Width / Math.Max(1d, bounds.Height);
                if (Math.Abs(dx) >= Math.Abs(dy))
                {
                    height = width / ratio;
                    if (_layerDragMode is DragMode.TopLeft or DragMode.TopRight) y = bounds.Bottom - height;
                }
                else
                {
                    width = height * ratio;
                    if (_layerDragMode is DragMode.TopLeft or DragMode.BottomLeft) x = bounds.Right - width;
                }
                width = Math.Max(MinimumLayerSize, width);
                height = Math.Max(MinimumLayerSize, height);
            }
        }

        layer.SetBounds((int)Math.Round(x), (int)Math.Round(y),
            Math.Max(MinimumLayerSize, (int)Math.Round(width)),
            Math.Max(MinimumLayerSize, (int)Math.Round(height)));
        _layerSelection = new Rect(layer.X, layer.Y, layer.Width, layer.Height);
        DrawLayerSelection();
        e.Handled = true;
    }

    private void LayerOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isLayerDragging) return;
        _isLayerDragging = false;
        LayerOverlay.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static DragMode GetLayerDragMode(Point point, Rect bounds)
    {
        bool left = Math.Abs(point.X - bounds.Left) <= LayerHandleRadius * 2;
        bool right = Math.Abs(point.X - bounds.Right) <= LayerHandleRadius * 2;
        bool top = Math.Abs(point.Y - bounds.Top) <= LayerHandleRadius * 2;
        bool bottom = Math.Abs(point.Y - bounds.Bottom) <= LayerHandleRadius * 2;
        if (left && top) return DragMode.TopLeft;
        if (right && top) return DragMode.TopRight;
        if (left && bottom) return DragMode.BottomLeft;
        if (right && bottom) return DragMode.BottomRight;
        if (left && point.Y >= bounds.Top && point.Y <= bounds.Bottom) return DragMode.Left;
        if (right && point.Y >= bounds.Top && point.Y <= bounds.Bottom) return DragMode.Right;
        if (top && point.X >= bounds.Left && point.X <= bounds.Right) return DragMode.Top;
        if (bottom && point.X >= bounds.Left && point.X <= bounds.Right) return DragMode.Bottom;
        return bounds.Contains(point) ? DragMode.Move : DragMode.None;
    }

    private void DrawLayerSelection()
    {
        LayerOverlay.Children.Clear();
        if (ViewModel?.SelectedLayer is not { IsVisible: true } layer) return;
        var rect = new Rect(layer.X, layer.Y, layer.Width, layer.Height);
        var border = new Rectangle
        {
            Width = rect.Width, Height = rect.Height,
            Stroke = Brushes.DodgerBlue, StrokeThickness = 1.5,
            StrokeDashArray = [3, 2], Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(border, rect.Left);
        Canvas.SetTop(border, rect.Top);
        LayerOverlay.Children.Add(border);
        foreach (var x in new[] { rect.Left, rect.Right })
        foreach (var y in new[] { rect.Top, rect.Bottom })
        {
            var handle = new Rectangle
            {
                Width = LayerHandleRadius * 2, Height = LayerHandleRadius * 2,
                Fill = Brushes.White, Stroke = Brushes.DodgerBlue, StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(handle, x - LayerHandleRadius);
            Canvas.SetTop(handle, y - LayerHandleRadius);
            LayerOverlay.Children.Add(handle);
        }
        foreach (var point in new[]
        {
            new Point(rect.Left + rect.Width / 2, rect.Top),
            new Point(rect.Left + rect.Width / 2, rect.Bottom),
            new Point(rect.Left, rect.Top + rect.Height / 2),
            new Point(rect.Right, rect.Top + rect.Height / 2)
        })
        {
            var handle = new Rectangle
            {
                Width = LayerHandleRadius * 2, Height = LayerHandleRadius * 2,
                Fill = Brushes.White, Stroke = Brushes.DodgerBlue, StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(handle, point.X - LayerHandleRadius);
            Canvas.SetTop(handle, point.Y - LayerHandleRadius);
            LayerOverlay.Children.Add(handle);
        }
    }

    private void Grayscale_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) ViewModel.SelectedFilter = ImageFilter.Grayscale;
    }

    private void Sepia_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) ViewModel.SelectedFilter = ImageFilter.Sepia;
    }
}

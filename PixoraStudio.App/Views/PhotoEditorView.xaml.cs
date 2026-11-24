using PixoraStudio.App.ViewModels;
using PixoraStudio.Core.Models;
using PixoraStudio.Core.Geometry;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
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
    private double _layerAspectRatioAtEditStart = 1d;
    private Rect _layerBoundsAtDragStart;
    private DragMode _layerDragMode;
    private bool _isLayerDragging;
    private const int DrawToolIndex = 10;
    private bool _isDrawingStroke;
    private readonly List<SKPoint> _drawStrokePoints = [];
    private Polyline? _drawStrokePreview;
    private const double MinimumLayerOverlayWorkspacePadding = 512;
    private double _layerOverlayWorkspacePadding = MinimumLayerOverlayWorkspacePadding;
    private Point _layerListDragStart;
    private PhotoLayerViewModel? _layerListDragSource;
    private bool _gpuRenderingDisabled = string.Equals(
        Environment.GetEnvironmentVariable("PIXORA_DISABLE_GPU_PREVIEW"), "1",
        StringComparison.Ordinal);

    private enum DragMode { None, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    public PhotoEditorView()
    {
        InitializeComponent();
        if (_gpuRenderingDisabled)
        {
            GpuPreviewHost.Child = null;
            CpuPreviewImage.Visibility = Visibility.Visible;
        }
    }

    private PhotoEditorViewModel? ViewModel => DataContext as PhotoEditorViewModel;

    private PhotoEditorViewModel? _observedViewModel;

    private void PhotoEditorView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _observedViewModel.PreviewInvalidated -= ViewModel_PreviewInvalidated;
        }
        _observedViewModel = e.NewValue as PhotoEditorViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _observedViewModel.PreviewInvalidated += ViewModel_PreviewInvalidated;
        }
        UpdateLayerOverlayWorkspace();
        DrawLayerSelection();
        GpuPreview?.InvalidateVisual();
    }

    private void PhotoEditorView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (_isDrawingStroke && e.Key == Key.Escape)
        {
            CancelBrushStroke();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            if (Keyboard.FocusedElement is TextBoxBase or ComboBox or PasswordBox)
                return;

            if (ViewModel is not { } vm || vm.SelectedLayer is not { } selectedLayer)
                return;

            vm.RemoveLayerCommand.Execute(selectedLayer);
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase or ComboBox or PasswordBox ||
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            return;

        switch (e.Key)
        {
            case Key.C:
                ViewModel?.CopyImageToClipboard();
                e.Handled = true;
                break;

            case Key.V:
                ViewModel?.PasteImageFromClipboard();
                e.Handled = true;
                break;

            case Key.Z:
                ViewModel?.UndoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Y:
                ViewModel?.RedoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.OemMinus:
            case Key.Subtract:
                ViewModel?.ZoomOutCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.OemPlus:
            case Key.Add:
                ViewModel?.ZoomInCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void PhotoEditorView_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (IsLoaded)
                Keyboard.Focus(this);
        }));

        UpdateLayerOverlayWorkspace();
        DrawLayerSelection();
        if (_gpuRenderingDisabled)
        {
            CpuPreviewImage.Visibility = Visibility.Visible;
            return;
        }
        GpuPreview.InvalidateVisual();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PhotoEditorViewModel.ImageWidth) or nameof(PhotoEditorViewModel.ImageHeight) or nameof(PhotoEditorViewModel.Zoom))
            Dispatcher.Invoke(UpdateLayerOverlayWorkspace);

        if (e.PropertyName == nameof(PhotoEditorViewModel.SelectedLayer))
            Dispatcher.Invoke(() =>
            {
                UpdateLayerOverlayWorkspace();
                DrawLayerSelection();
            });

        GpuPreview?.InvalidateVisual();
    }

    /// <summary>Keep the layer handles aligned with document coordinates.</summary>
    /// <summary>
    /// Expands the interactive selection workspace around the fixed document
    /// so layer bounds and resize handles remain reachable outside the canvas.
    /// </summary>
    private void UpdateLayerOverlayWorkspace()
    {
        if (DocumentCanvas is null || LayerOverlay is null)
            return;

        if (ViewModel is not { } vm)
        {
            DocumentCanvas.Margin = new Thickness(_layerOverlayWorkspacePadding);
            LayerOverlay.Width = 1;
            LayerOverlay.Height = 1;
            return;
        }

        _layerOverlayWorkspacePadding = MinimumLayerOverlayWorkspacePadding;
        if (vm.SelectedLayer is { } selectedLayer)
        {
            var overflow = Math.Max(
                Math.Max(Math.Max(0, -selectedLayer.X), Math.Max(0, -selectedLayer.Y)),
                Math.Max(
                    Math.Max(0, selectedLayer.X + selectedLayer.Width - vm.ImageWidth),
                    Math.Max(0, selectedLayer.Y + selectedLayer.Height - vm.ImageHeight)));
            _layerOverlayWorkspacePadding = Math.Max(
                MinimumLayerOverlayWorkspacePadding, overflow + LayerHandleRadius * 4);
        }

        // RenderTransform does not contribute to WPF's scroll extent, so
        // include both the scaled canvas and the scaled off-canvas workspace.
        var zoom = Math.Max(0.01, vm.Zoom);
        var horizontalPadding = _layerOverlayWorkspacePadding * zoom +
            Math.Max(0, vm.ImageWidth * (zoom - 1) / 2);
        var verticalPadding = _layerOverlayWorkspacePadding * zoom +
            Math.Max(0, vm.ImageHeight * (zoom - 1) / 2);
        DocumentCanvas.Margin = new Thickness(
            horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);
        LayerOverlay.Width = Math.Max(1, vm.ImageWidth + _layerOverlayWorkspacePadding * 2);
        LayerOverlay.Height = Math.Max(1, vm.ImageHeight + _layerOverlayWorkspacePadding * 2);
    }

    private void ViewModel_PreviewInvalidated(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            GpuPreview?.InvalidateVisual();
        else
            Dispatcher.BeginInvoke(new Action(() => GpuPreview?.InvalidateVisual()));
    }

    private void GpuPreview_PaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        if (_gpuRenderingDisabled)
            return;

        if (ViewModel is not { IsImageLoaded: true } vm)
        {
            e.Surface.Canvas.Clear(SKColors.Transparent);
            return;
        }

        try
        {
            var canvas = e.Surface.Canvas;
            var surfaceSize = GpuPreview.CanvasSize;
            if (vm.ImageWidth > 0 && vm.ImageHeight > 0 &&
                surfaceSize.Width > 0 && surfaceSize.Height > 0)
            {
                canvas.Scale(surfaceSize.Width / vm.ImageWidth,
                    surfaceSize.Height / vm.ImageHeight);
            }
            vm.RenderGpuPreview(canvas);
            vm.SetGpuPreviewAvailable(true);
            CpuPreviewImage.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            // Keep the editor usable on systems without a compatible OpenGL
            // context or when a driver rejects the runtime shader.
            DisableGpuPreview(vm);
        }
    }



    private void GpuPreview_RenderFailed(object? sender, EventArgs e) =>
        DisableGpuPreview(ViewModel);

    private void DisableGpuPreview(PhotoEditorViewModel? viewModel)
    {
        if (_gpuRenderingDisabled) return;
        _gpuRenderingDisabled = true;
        CpuPreviewImage.Visibility = Visibility.Visible;
        viewModel?.SetGpuPreviewAvailable(false);

        // Removing the GL control raises Unloaded, releasing its OpenGL and
        // Direct3D interop resources after the current paint callback returns.
        Dispatcher.BeginInvoke(new Action(() => GpuPreviewHost.Child = null));
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !int.TryParse(tag, out int selectedTool) ||
            selectedTool < 0 || selectedTool >= ToolSettingsTabs.Items.Count)
            return;

        ToolSettingsTabs.SelectedIndex = selectedTool;
        LayerOverlay.Cursor = selectedTool == DrawToolIndex ? Cursors.Pen : Cursors.Arrow;
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
        if (CropOverlay.Visibility == Visibility.Visible)
        {
            EndCropMode();
            return;
        }

        if (ViewModel is not { IsImageLoaded: true, SelectedLayer: { } layer } vm ||
            layer.Width < 1 || layer.Height < 1)
            return;

        var insetX = Math.Max(1, layer.Width * 0.1);
        var insetY = Math.Max(1, layer.Height * 0.1);
        _cropSelection = new Rect(layer.X + insetX, layer.Y + insetY,
            Math.Max(MinimumCropSize, layer.Width - insetX * 2),
            Math.Max(MinimumCropSize, layer.Height - insetY * 2));
        _cropSelection.Intersect(new Rect(layer.X, layer.Y, layer.Width, layer.Height));
        _cropSelection = ApplySelectedAspectRatio(_cropSelection, vm);
        CropOverlay.Visibility = Visibility.Visible;
        LayerOverlay.Visibility = Visibility.Collapsed;
        CropActionPanel.Visibility = Visibility.Visible;
        VisualCropToggleButton.Content = "Cancel visual crop";
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

        if (vm.SelectedLayer is not { } layer) return selection;
        float ratio = tag == "original"
            ? (float)layer.Width / Math.Max(1, layer.Height)
            : float.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);

        var adjusted = CropSelectionGeometry.ConstrainToAspectRatio(
            new SKRect((float)selection.Left, (float)selection.Top,
                       (float)selection.Right, (float)selection.Bottom),
            new SKRect(layer.X, layer.Y, layer.X + layer.Width, layer.Y + layer.Height), ratio);

        return new Rect(adjusted.Left, adjusted.Top, adjusted.Width, adjusted.Height);
    }

    private void EndCropMode()
    {
        CropOverlay.Visibility = Visibility.Collapsed;
        LayerOverlay.Visibility = Visibility.Visible;
        CropActionPanel.Visibility = Visibility.Collapsed;
        VisualCropToggleButton.Content = "Start visual crop";
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
        if (vm.SelectedLayer is not { } layer) return;
        var bounds = new Rect(layer.X, layer.Y, layer.Width, layer.Height);
        var selection = _selectionAtDragStart;

        switch (_dragMode)
        {
            case DragMode.Move:
                selection.X = Math.Clamp(selection.X + dx, bounds.Left, bounds.Right - selection.Width);
                selection.Y = Math.Clamp(selection.Y + dy, bounds.Top, bounds.Bottom - selection.Height);
                break;
            case DragMode.Left:
            case DragMode.TopLeft:
            case DragMode.BottomLeft:
                selection = ResizeLeft(selection, dx, bounds.Left);
                break;
        }
        if (_dragMode is DragMode.Right or DragMode.TopRight or DragMode.BottomRight)
            selection.Width = Math.Clamp(_selectionAtDragStart.Width + dx, MinimumCropSize, bounds.Right - selection.Left);
        if (_dragMode is DragMode.Top or DragMode.TopLeft or DragMode.TopRight)
            selection = ResizeTop(selection, dy, bounds.Top);
        if (_dragMode is DragMode.Bottom or DragMode.BottomLeft or DragMode.BottomRight)
            selection.Height = Math.Clamp(_selectionAtDragStart.Height + dy, MinimumCropSize, bounds.Bottom - selection.Top);

        _cropSelection = ApplySelectedAspectRatio(selection, vm);
        DrawCropOverlay();
        UpdateCropCoordinates();
    }

    private Rect ResizeLeft(Rect rect, double dx, double minimumLeft = 0)
    {
        var right = rect.Right;
        var left = Math.Clamp(rect.Left + dx, minimumLeft, right - MinimumCropSize);
        rect.X = left;
        rect.Width = right - left;
        return rect;
    }

    private Rect ResizeTop(Rect rect, double dy, double minimumTop = 0)
    {
        var bottom = rect.Bottom;
        var top = Math.Clamp(rect.Top + dy, minimumTop, bottom - MinimumCropSize);
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
        if (vm.SelectedLayer is not { } layer) return;
        vm.CropX = Math.Max(0, (int)Math.Round(_cropSelection.Left - layer.X));
        vm.CropY = Math.Max(0, (int)Math.Round(_cropSelection.Top - layer.Y));
        vm.CropWidth = Math.Max(1, (int)Math.Round(_cropSelection.Width));
        vm.CropHeight = Math.Max(1, (int)Math.Round(_cropSelection.Height));
    }


    private void PhotoEditorView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (IsLayerOptionsButton(source) ||
            IsLayerPropertiesPopupSource(source) ||
            ViewModel is not { } vm)
            return;

        foreach (var layer in vm.AdditionalLayers)
            layer.IsPropertiesOpen = false;
    }

    private static bool IsLayerPropertiesPopupSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "LayerPropertiesPopupContent" })
                return true;

            source = source switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return false;
    }

    private static bool IsLayerOptionsButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Name: "LayerOptionsButton" })
                return true;

            source = source switch
            {
                Visual visual => VisualTreeHelper.GetParent(visual),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => null
            };
        }

        return false;
    }

    private void PickBrushColor_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        var picker = new ColorPickerDialogWindow(viewModel.BrushColor);
        if (Window.GetWindow(this) is { } owner)
            picker.Owner = owner;

        if (picker.ShowDialog() == true)
            viewModel.BrushColor = picker.SelectedColor;
        e.Handled = true;
    }

    private void PickFillColor_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedLayer is not { } layer)
            return;

        var picker = new ColorPickerDialogWindow(layer.FillColor);
        if (Window.GetWindow(this) is { } owner)
            picker.Owner = owner;

        if (picker.ShowDialog() == true)
            layer.FillColor = picker.SelectedColor;
        e.Handled = true;
    }

    private void PickStrokeColor_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedLayer is not { } layer)
            return;

        var picker = new ColorPickerDialogWindow(layer.StrokeColor);
        if (Window.GetWindow(this) is { } owner)
            picker.Owner = owner;

        if (picker.ShowDialog() == true)
            layer.StrokeColor = picker.SelectedColor;
        e.Handled = true;
    }

    private void LayerItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Slider or TextBox or System.Windows.Controls.Primitives.ButtonBase)
            return;

        if (sender is not FrameworkElement { DataContext: PhotoLayerViewModel layer } ||
            ViewModel is not { } vm)
            return;

        vm.SelectLayer(layer);
        _layerListDragSource = layer;
        _layerListDragStart = e.GetPosition(this);
        DrawLayerSelection();
        e.Handled = true;
    }

    private void LayerPropertiesPopup_Opened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup ||
            popup.PlacementTarget is not FrameworkElement { DataContext: PhotoLayerViewModel layer } ||
            popup.Child is not FrameworkElement popupContent)
            return;

        popupContent.DataContext = layer;
    }

    private void LayerProperties_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PhotoLayerViewModel layer } ||
            ViewModel is not { } vm)
            return;

        var shouldOpen = !layer.IsPropertiesOpen;
        foreach (var item in vm.AdditionalLayers)
            item.IsPropertiesOpen = false;

        vm.SelectLayer(layer);
        layer.IsPropertiesOpen = shouldOpen;
        e.Handled = true;
    }

    private void CloseLayerProperties_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoLayerViewModel layer })
            layer.IsPropertiesOpen = false;
        e.Handled = true;
    }

    private void DuplicateLayerFromPopup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoLayerViewModel layer } &&
            ViewModel is { } vm)
        {
            layer.IsPropertiesOpen = false;
            vm.DuplicateLayerCommand.Execute(layer);
        }
        e.Handled = true;
    }

    private void RemoveLayerFromPopup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhotoLayerViewModel layer } &&
            ViewModel is { } vm)
        {
            layer.IsPropertiesOpen = false;
            vm.RemoveLayerCommand.Execute(layer);
        }
        e.Handled = true;
    }

    private void LayerOpacitySlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || slider.DataContext is not PhotoLayerViewModel)
            return;

        slider.CaptureMouse();
        SetLayerOpacityFromSliderPointer(slider, e.GetPosition(slider));
        e.Handled = true;
    }

    private void LayerOpacitySlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider ||
            !slider.IsMouseCaptured ||
            e.LeftButton != MouseButtonState.Pressed)
            return;

        SetLayerOpacityFromSliderPointer(slider, e.GetPosition(slider));
        e.Handled = true;
    }

    private void LayerOpacitySlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && slider.IsMouseCaptured)
        {
            SetLayerOpacityFromSliderPointer(slider, e.GetPosition(slider));
            slider.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private static void SetLayerOpacityFromSliderPointer(Slider slider, Point pointerPosition)
    {
        if (slider.DataContext is not PhotoLayerViewModel layer || slider.ActualWidth <= 0)
            return;

        var percent = Math.Clamp(pointerPosition.X / slider.ActualWidth * 100d, 0d, 100d);
        layer.OpacityPercent = percent;
    }

    private void LayerItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.OriginalSource is Slider or TextBox or System.Windows.Controls.Primitives.ButtonBase ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: PhotoLayerViewModel layer })
            return;

        var point = e.GetPosition(this);
        if (_layerListDragSource is null)
        {
            _layerListDragSource = layer;
            _layerListDragStart = point;
            return;
        }

        if (!ReferenceEquals(_layerListDragSource, layer))
            return;

        if (Math.Abs(point.X - _layerListDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _layerListDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _layerListDragSource = null;
        var data = new DataObject(typeof(PhotoLayerViewModel), layer);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
    }

    private void LayerItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PhotoLayerViewModel))
            ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void LayerItem_Drop(object sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm ||
            sender is not FrameworkElement { DataContext: PhotoLayerViewModel target } ||
            e.Data.GetData(typeof(PhotoLayerViewModel)) is not PhotoLayerViewModel source)
            return;

        int oldIndex = vm.AdditionalLayers.IndexOf(source);
        int newIndex = vm.AdditionalLayers.IndexOf(target);
        if (oldIndex >= 0 && newIndex >= 0)
            vm.MoveLayer(oldIndex, newIndex);
        e.Handled = true;
    }

    private void BeginBrushStroke(PhotoEditorViewModel viewModel, Point overlayPoint, MouseButtonEventArgs e)
    {
        double x = overlayPoint.X - _layerOverlayWorkspacePadding;
        double y = overlayPoint.Y - _layerOverlayWorkspacePadding;
        if (x < 0 || y < 0 || x > viewModel.ImageWidth || y > viewModel.ImageHeight)
            return;

        _drawStrokePoints.Clear();
        _drawStrokePoints.Add(new SKPoint((float)x, (float)y));
        var previewPoint = new Point(
            x + _layerOverlayWorkspacePadding,
            y + _layerOverlayWorkspacePadding);
        _drawStrokePreview = new Polyline
        {
            Stroke = (Brush)new BrushConverter().ConvertFromString(viewModel.BrushColor)!,
            StrokeThickness = Math.Clamp(viewModel.BrushSize, 1d, 100d),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        _drawStrokePreview.Points.Add(previewPoint);
        _drawStrokePreview.Points.Add(previewPoint);
        LayerOverlay.Children.Add(_drawStrokePreview);
        _isDrawingStroke = true;
        LayerOverlay.CaptureMouse();
        e.Handled = true;
    }

    private void CancelBrushStroke()
    {
        if (!_isDrawingStroke)
            return;

        _isDrawingStroke = false;
        LayerOverlay.ReleaseMouseCapture();
        _drawStrokePoints.Clear();
        _drawStrokePreview = null;
        DrawLayerSelection();
    }

    private void LayerOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsImageLoaded: true } vm || CropOverlay.Visibility == Visibility.Visible) return;
        var overlayPoint = e.GetPosition(LayerOverlay);
        var point = new Point(
            overlayPoint.X - _layerOverlayWorkspacePadding,
            overlayPoint.Y - _layerOverlayWorkspacePadding);
        if (ToolSettingsTabs.SelectedIndex == DrawToolIndex)
        {
            BeginBrushStroke(vm, overlayPoint, e);
            return;
        }

        _layerDragMode = DragMode.None;
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

        // Selection may expand the workspace; convert using the updated padding.
        point = new Point(
            overlayPoint.X - _layerOverlayWorkspacePadding,
            overlayPoint.Y - _layerOverlayWorkspacePadding);
        if (selected!.IsLocked)
        {
            DrawLayerSelection();
            return;
        }

        vm.BeginLayerGeometryChange();
        _layerBoundsAtDragStart = new Rect(selected!.X, selected.Y, selected.Width, selected.Height);
        _layerSelection = _layerBoundsAtDragStart;
        _layerDragStart = point;
        _isLayerDragging = true;
        vm.BeginLayerDragPreview();
        LayerOverlay.CaptureMouse();
        DrawLayerSelection();
        e.Handled = true;
    }

    private void LayerOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDrawingStroke)
        {
            if (ViewModel is not { IsImageLoaded: true } drawingViewModel)
            {
                CancelBrushStroke();
                return;
            }

            var drawOverlayPosition = e.GetPosition(LayerOverlay);
            float drawX = (float)Math.Clamp(
                drawOverlayPosition.X - _layerOverlayWorkspacePadding, 0d, drawingViewModel.ImageWidth);
            float drawY = (float)Math.Clamp(
                drawOverlayPosition.Y - _layerOverlayWorkspacePadding, 0d, drawingViewModel.ImageHeight);
            _drawStrokePoints.Add(new SKPoint(drawX, drawY));
            _drawStrokePreview?.Points.Add(new Point(
                drawX + _layerOverlayWorkspacePadding,
                drawY + _layerOverlayWorkspacePadding));
            e.Handled = true;
            return;
        }

        if (!_isLayerDragging || ViewModel?.SelectedLayer is not { } layer) return;
        var overlayPoint = e.GetPosition(LayerOverlay);
        var point = new Point(
            overlayPoint.X - _layerOverlayWorkspacePadding,
            overlayPoint.Y - _layerOverlayWorkspacePadding);
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

            if (ViewModel is { IsSnappingEnabled: true } vm)
            {
                var zoom = Math.Max(0.01, vm.Zoom);
                var otherLayers = vm.AdditionalLayers
                    .Where(other => !ReferenceEquals(other, layer) && other.IsVisible)
                    .Select(other => new LayerSnapBounds(other.X, other.Y, other.Width, other.Height));
                var snapped = LayerSnapping.Snap(x, y, width, height,
                    vm.ImageWidth, vm.ImageHeight, otherLayers, 8d / zoom);
                x = snapped.X;
                y = snapped.Y;
                DrawSnapGuides(snapped.VerticalGuide, snapped.HorizontalGuide);
            }
            else
            {
                SnapGuideOverlay.Children.Clear();
            }
        }
        else
        {
            SnapGuideOverlay.Children.Clear();
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

            var ratio = bounds.Width / Math.Max(1d, bounds.Height);
            if (_layerDragMode is DragMode.TopLeft or DragMode.TopRight or DragMode.BottomLeft or DragMode.BottomRight)
            {
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
            }
            else if (_layerDragMode is DragMode.Left or DragMode.Right)
            {
                height = width / ratio;
                y = bounds.Top + (bounds.Height - height) / 2d;
            }
            else if (_layerDragMode is DragMode.Top or DragMode.Bottom)
            {
                width = height * ratio;
                x = bounds.Left + (bounds.Width - width) / 2d;
            }
            width = Math.Max(MinimumLayerSize, width);
            height = Math.Max(MinimumLayerSize, height);
        }

        layer.SetBounds((int)Math.Round(x), (int)Math.Round(y),
            Math.Max(MinimumLayerSize, (int)Math.Round(width)),
            Math.Max(MinimumLayerSize, (int)Math.Round(height)));
        _layerSelection = new Rect(layer.X, layer.Y, layer.Width, layer.Height);
        UpdateLayerOverlayWorkspace();
        DrawLayerSelection();
        ViewModel?.SetLayerDragPreviewBounds(layer.X, layer.Y, layer.Width, layer.Height);
        e.Handled = true;
    }

    private void LayerOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDrawingStroke)
        {
            _isDrawingStroke = false;
            LayerOverlay.ReleaseMouseCapture();
            var points = _drawStrokePoints.ToArray();
            _drawStrokePoints.Clear();
            _drawStrokePreview = null;
            ViewModel?.AddBrushStroke(points);
            UpdateLayerOverlayWorkspace();
            DrawLayerSelection();
            e.Handled = true;
            return;
        }

        if (!_isLayerDragging) return;
        _isLayerDragging = false;
        LayerOverlay.ReleaseMouseCapture();
        ViewModel?.EndLayerDragPreview();
        ViewModel?.CommitLayerGeometryChange();
        UpdateLayerOverlayWorkspace();
        DrawLayerSelection();
        SnapGuideOverlay.Children.Clear();
        e.Handled = true;
    }

    private void DrawSnapGuides(double? vertical, double? horizontal)
    {
        SnapGuideOverlay.Children.Clear();
        if (vertical is double x)
        {
            var line = new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = ViewModel?.ImageHeight ?? 0,
                Stroke = Brushes.Magenta, StrokeThickness = 1,
                StrokeDashArray = [3, 2], IsHitTestVisible = false
            };
            SnapGuideOverlay.Children.Add(line);
        }
        if (horizontal is double y)
        {
            var line = new Line
            {
                X1 = 0, X2 = ViewModel?.ImageWidth ?? 0, Y1 = y, Y2 = y,
                Stroke = Brushes.Magenta, StrokeThickness = 1,
                StrokeDashArray = [3, 2], IsHitTestVisible = false
            };
            SnapGuideOverlay.Children.Add(line);
        }
    }

    private DragMode GetLayerDragMode(Point point, Rect bounds)
    {
        if (ViewModel is null) return DragMode.None;

        var layerBounds = new SKRect((float)bounds.Left, (float)bounds.Top,
            (float)bounds.Right, (float)bounds.Bottom);
        var handleIndex = LayerSelectionGeometry.FindHandle(
            new SKPoint((float)point.X, (float)point.Y),
            layerBounds, (float)(LayerHandleRadius * 2));

        return handleIndex switch
        {
            0 => DragMode.TopLeft,
            1 => DragMode.Top,
            2 => DragMode.TopRight,
            3 => DragMode.Right,
            4 => DragMode.BottomRight,
            5 => DragMode.Bottom,
            6 => DragMode.BottomLeft,
            7 => DragMode.Left,
            _ => bounds.Contains(point) ? DragMode.Move : DragMode.None
        };
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
        Canvas.SetLeft(border, rect.Left + _layerOverlayWorkspacePadding);
        Canvas.SetTop(border, rect.Top + _layerOverlayWorkspacePadding);
        LayerOverlay.Children.Add(border);
        var layerBounds = new SKRect((float)rect.Left, (float)rect.Top,
            (float)rect.Right, (float)rect.Bottom);
        // Draw handles at the layer's real bounds. The expanded overlay gives
        // off-canvas positions their own render and hit-testable coordinate space.
        foreach (var point in LayerSelectionGeometry.GetHandlePoints(layerBounds))
        {
            var handle = new Rectangle
            {
                Width = LayerHandleRadius * 2, Height = LayerHandleRadius * 2,
                Fill = Brushes.White, Stroke = Brushes.DodgerBlue, StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(handle, point.X + _layerOverlayWorkspacePadding - LayerHandleRadius);
            Canvas.SetTop(handle, point.Y + _layerOverlayWorkspacePadding - LayerHandleRadius);
            LayerOverlay.Children.Add(handle);
        }
    }

    private void LayerProperty_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var viewModel = ViewModel;
        var layer = viewModel?.SelectedLayer;
        if (viewModel is null || layer is null)
            return;

        viewModel.BeginLayerGeometryChange();
        _layerAspectRatioAtEditStart = layer.Width / (double)Math.Max(1, layer.Height);
    }

    private void LayerProperty_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var viewModel = ViewModel;
        if (sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

            if (textBox.Tag is string dimension &&
                dimension is "Width" or "Height" &&
                viewModel?.SelectedLayer is { IsEditable: true } layer)
            {
                int width = Math.Max(1, layer.Width);
                int height = Math.Max(1, layer.Height);
                if (viewModel.LockAspectRatio && _layerAspectRatioAtEditStart > 0d &&
                    double.IsFinite(_layerAspectRatioAtEditStart))
                {
                    if (dimension == "Width")
                        height = Math.Max(1, (int)Math.Round(width / _layerAspectRatioAtEditStart));
                    else
                        width = Math.Max(1, (int)Math.Round(height * _layerAspectRatioAtEditStart));
                }

                layer.SetBounds(layer.X, layer.Y, width, height);
            }
        }

        viewModel?.CommitLayerGeometryChange();
    }

    private void SelectLayerOpacitySlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || ViewModel?.SelectedLayer is not { IsEditable: true })
            return;

        ViewModel.BeginLayerGeometryChange();
        slider.CaptureMouse();
        SetSelectLayerOpacityFromPointer(slider, e.GetPosition(slider));
        e.Handled = true;
    }

    private void SelectLayerOpacitySlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider ||
            !slider.IsMouseCaptured ||
            e.LeftButton != MouseButtonState.Pressed)
            return;

        SetSelectLayerOpacityFromPointer(slider, e.GetPosition(slider));
        e.Handled = true;
    }

    private void SelectLayerOpacitySlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsMouseCaptured)
            return;

        SetSelectLayerOpacityFromPointer(slider, e.GetPosition(slider));
        slider.ReleaseMouseCapture();
        ViewModel?.CommitLayerGeometryChange();
        e.Handled = true;
    }

    private static void SetSelectLayerOpacityFromPointer(Slider slider, Point pointerPosition)
    {
        if (slider.ActualWidth <= 0)
            return;

        double percent = pointerPosition.X / slider.ActualWidth;
        slider.Value = slider.Minimum + Math.Clamp(percent, 0d, 1d) * (slider.Maximum - slider.Minimum);
    }

    private void SelectLayerOpacitySlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() => ViewModel?.CommitLayerGeometryChange()),
            DispatcherPriority.Background);
    }

    private void SelectLayerOpacitySlider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ViewModel?.BeginLayerGeometryChange();
    }

    private void SelectLayerOpacitySlider_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        ViewModel?.CommitLayerGeometryChange();
    }

    private void SelectLayerOpacitySlider_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ViewModel?.BeginLayerGeometryChange();
    }

    private void SelectLayerOpacitySlider_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Slider slider)
            slider.GetBindingExpression(Slider.ValueProperty)?.UpdateSource();

        ViewModel?.CommitLayerGeometryChange();
    }

    private void AdjustmentSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.BeginAdjustmentChange();
    }

    private void AdjustmentSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ScheduleAdjustmentCommit();
    }

    private void AdjustmentSlider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ViewModel?.BeginAdjustmentChange();
    }

    private void AdjustmentSlider_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        ViewModel?.CommitAdjustmentChange();
    }

    private void AdjustmentSlider_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ViewModel?.BeginAdjustmentChange();
    }

    private void AdjustmentSlider_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ViewModel?.CommitAdjustmentChange();
    }

    private void AdjustmentSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        ScheduleAdjustmentCommit();
    }

    private void ScheduleAdjustmentCommit()
    {
        Dispatcher.BeginInvoke(new Action(() => ViewModel?.CommitAdjustmentChange()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void AdjustmentValueBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ViewModel?.BeginAdjustmentChange();
    }

    private void AdjustmentValueBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.BeginAdjustmentChange();
    }

    private void AdjustmentValueBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        ViewModel?.BeginAdjustmentChange();
        if (e.Key != Key.Enter) return;

        if (sender is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        ViewModel?.CommitAdjustmentChange();
        e.Handled = true;
    }

    private void AdjustmentValueBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        ViewModel?.CommitAdjustmentChange();
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

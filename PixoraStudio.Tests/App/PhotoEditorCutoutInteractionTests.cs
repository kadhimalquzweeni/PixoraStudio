using System.Xml.Linq;

namespace PixoraStudio.Tests.App;

public sealed class PhotoEditorCutoutInteractionTests
{
    [Fact]
    public void ApplyCutoutButton_ShouldBeDefaultButtonAndUseClickHandler()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml");
        Assert.True(File.Exists(path), $"Expected photo editor view at {path}");

        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(path);
        var applyButton = document.Descendants(presentation + "Button")
            .Single(button => (string?)button.Attribute("Content") == "Apply cutout");

        Assert.Equal("True", (string?)applyButton.Attribute("IsDefault"));
        Assert.Equal("ApplyCutout_Click", (string?)applyButton.Attribute("Click"));
        Assert.Equal("{Binding HasCutoutSelection}", (string?)applyButton.Attribute("IsEnabled"));
    }

    [Fact]
    public void BeginCropMode_ShouldInitializeSelectionToEntireSelectedLayer()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml.cs");
        Assert.True(File.Exists(path), $"Expected photo editor code-behind at {path}");

        var source = File.ReadAllText(path);
        int begin = source.IndexOf("private void BeginCropMode()", StringComparison.Ordinal);
        int end = source.IndexOf("private void ApplyCrop_Click", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "Expected crop mode initialization method.");

        string method = source[begin..end];
        Assert.Contains("_cropSelection = new Rect(layer.X, layer.Y, layer.Width, layer.Height);", method);
        Assert.DoesNotContain("insetX", method);
        Assert.DoesNotContain("insetY", method);
    }

    [Fact]
    public void DrawLayerSelection_ShouldHideLayerHandlesWhileCropOverlayIsVisible()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml.cs");
        Assert.True(File.Exists(path), $"Expected photo editor code-behind at {path}");

        var source = File.ReadAllText(path);
        int begin = source.IndexOf("private void DrawLayerSelection()", StringComparison.Ordinal);
        int end = source.IndexOf("private void LayerProperty_GotKeyboardFocus", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "Expected layer selection drawing method.");

        string method = source[begin..end];
        Assert.Contains("CropOverlay?.Visibility == Visibility.Visible", method);
        Assert.Contains("LayerOverlay.Visibility = Visibility.Collapsed;", method);
        Assert.Contains("LayerOverlay.Children.Clear();", method);
    }

    [Fact]
    public void CropMode_ShouldHideAndRestoreLayerResizePreview()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml");
        var codeBehindPath = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml.cs");
        Assert.True(File.Exists(xamlPath), $"Expected photo editor view at {xamlPath}");
        Assert.True(File.Exists(codeBehindPath), $"Expected photo editor code-behind at {codeBehindPath}");

        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(xamlPath);
        var previewOverlay = document.Descendants(presentation + "Canvas")
            .Single(canvas => (string?)canvas.Attribute(xaml + "Name") == "LayerDragPreviewOverlay");
        Assert.Contains(previewOverlay.Descendants(presentation + "Image"),
            image => (string?)image.Attribute("Source") == "{Binding LayerDragPreviewImage}");

        var source = File.ReadAllText(codeBehindPath);
        int beginCrop = source.IndexOf("private void BeginCropMode()", StringComparison.Ordinal);
        int endCrop = source.IndexOf("private void ApplyCrop_Click", beginCrop, StringComparison.Ordinal);
        int beginEnd = source.IndexOf("private void EndCropMode()", StringComparison.Ordinal);
        int endEnd = source.IndexOf("private void CropOverlay_MouseLeftButtonDown", beginEnd, StringComparison.Ordinal);
        Assert.True(beginCrop >= 0 && endCrop > beginCrop, "Expected crop initialization method.");
        Assert.True(beginEnd >= 0 && endEnd > beginEnd, "Expected crop exit method.");

        Assert.Contains("LayerDragPreviewOverlay.Visibility = Visibility.Collapsed;",
            source[beginCrop..endCrop]);
        Assert.Contains("LayerDragPreviewOverlay.Visibility = Visibility.Visible;",
            source[beginEnd..endEnd]);
    }

    [Fact]
    public void ClickingOutsideCanvas_ShouldNotExitCropModeOrRestoreResizePreview()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml.cs");
        Assert.True(File.Exists(path), $"Expected photo editor code-behind at {path}");

        var source = File.ReadAllText(path);
        int begin = source.IndexOf("private void PhotoEditorView_PreviewMouseDown", StringComparison.Ordinal);
        int end = source.IndexOf("private void ", begin + 1, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "Expected preview mouse-down handler.");

        string handler = source[begin..end];
        Assert.DoesNotContain("EndCropMode();", handler);
        Assert.Contains("LayerDragPreviewOverlay.Visibility = Visibility.Collapsed;", source);
    }

    [Fact]
    public void LayerResize_ShouldAllowFreeEdgeResizingAndSnapToCanvas()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml.cs");
        Assert.True(File.Exists(path), $"Expected photo editor code-behind at {path}");

        var source = File.ReadAllText(path);
        int begin = source.IndexOf("private void LayerOverlay_MouseMove", StringComparison.Ordinal);
        int end = source.IndexOf("private static bool TryFindResizeSnap", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "Expected layer resize interaction method.");

        string method = source[begin..end];
        Assert.Contains("if (_layerDragMode is DragMode.TopLeft or DragMode.TopRight or DragMode.BottomLeft or DragMode.BottomRight)", method);
        Assert.DoesNotContain("else if (_layerDragMode is DragMode.Left or DragMode.Right)", method);
        Assert.DoesNotContain("else if (_layerDragMode is DragMode.Top or DragMode.Bottom)", method);
        Assert.Contains("TryFindResizeSnap", method);
        Assert.Contains("resizeViewModel.ImageWidth / 2d", method);
        Assert.Contains("resizeViewModel.ImageHeight / 2d", method);
    }


    [Fact]
    public void PhotoEditorKeyboardHandler_ShouldApplyCutoutOnEnterWhenSelectionExists()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PhotoEditorView.xaml.cs");
        Assert.True(File.Exists(path), $"Expected photo editor code-behind at {path}");

        var source = File.ReadAllText(path);

        Assert.Contains("e.Key == Key.Enter", source);
        Assert.Contains("ToolSettingsTabs?.SelectedIndex == CutoutToolIndex", source);
        Assert.Contains("ViewModel is { HasCutoutSelection: true }", source);
        Assert.Contains("ApplyCutoutSelection();", source);
        Assert.Contains("private void ApplyCutout_Click", source);
    }
}

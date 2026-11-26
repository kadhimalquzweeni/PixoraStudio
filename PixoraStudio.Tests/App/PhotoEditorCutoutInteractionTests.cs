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

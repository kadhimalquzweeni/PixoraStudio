using System.Xml.Linq;

namespace PixoraStudio.Tests.App;

public sealed class PixoraThemeResourceTests
{
    [Fact]
    public void ApplicationResources_ShouldDefineSharedInputAndSliderStyles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "App.xaml");
        Assert.True(File.Exists(path), $"Expected application resources at {path}");

        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styles = document.Descendants(presentation + "Style")
            .Where(style => style.Attribute("TargetType") is not null)
            .Select(style => style.Attribute("TargetType")!.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("{x:Type TextBox}", styles);
        Assert.Contains("{x:Type ComboBox}", styles);
        Assert.Contains("{x:Type ComboBoxItem}", styles);
        Assert.Contains("{x:Type Slider}", styles);
        Assert.Contains("{x:Type CheckBox}", styles);
        Assert.Contains("{x:Type Label}", styles);
        Assert.Contains("{x:Type TextBlock}", styles);
        var comboBoxTemplate = document.Descendants(presentation + "Style")
            .First(style => (string?)style.Attribute("TargetType") == "{x:Type ComboBox}")
            .Descendants(presentation + "ControlTemplate")
            .FirstOrDefault();

        Assert.NotNull(comboBoxTemplate);
        Assert.NotNull(comboBoxTemplate!.Descendants(presentation + "Popup").FirstOrDefault());
        Assert.Contains(comboBoxTemplate.Descendants(presentation + "Border"),
            border => (string?)border.Attribute("Background") == "#25282F");

        var checkBoxTemplate = document.Descendants(presentation + "Style")
            .First(style => (string?)style.Attribute("TargetType") == "{x:Type CheckBox}")
            .Descendants(presentation + "ControlTemplate")
            .FirstOrDefault();

        Assert.NotNull(checkBoxTemplate);
        Assert.NotNull(checkBoxTemplate!.Descendants(presentation + "Path")
            .FirstOrDefault(path => (string?)path.Attribute("Name") == "CheckMark"));
        Assert.NotNull(checkBoxTemplate.Descendants(presentation + "Path")
            .FirstOrDefault(path => (string?)path.Attribute("Name") == "IndeterminateMark"));

        Assert.NotNull(document.Descendants(presentation + "Style")
            .FirstOrDefault(style => (string?)style.Attribute("TargetType") == "{x:Type Slider}")
            ?.Descendants(presentation + "ControlTemplate").FirstOrDefault());
    }
}

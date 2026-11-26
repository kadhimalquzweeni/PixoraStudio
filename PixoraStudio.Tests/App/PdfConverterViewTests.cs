using System.Xml.Linq;

namespace PixoraStudio.Tests.App;

public sealed class PdfConverterViewTests
{
    [Fact]
    public void PdfConverterView_ShouldExposePageSelectionAndExportSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PdfConverterView.xaml");
        Assert.True(File.Exists(path), $"Expected PDF converter view at {path}");

        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(document.Descendants(presentation + "ListBox"),
            list => (string?)list.Attribute("ItemsSource") == "{Binding Pages}");
        Assert.Contains(document.Descendants(presentation + "ComboBox"),
            combo => (string?)combo.Attribute("ItemsSource") == "{Binding FormatOptions}");
        Assert.Contains(document.Descendants(presentation + "ComboBox"),
            combo => (string?)combo.Attribute("ItemsSource") == "{Binding DpiOptions}");
        Assert.Contains(document.Descendants(presentation + "TextBlock"),
            label => (string?)label.Attribute("Text") == "{Binding OutputDimensionsLabel}");
        Assert.Contains(document.Descendants(presentation + "Slider"),
            slider => (string?)slider.Attribute("Value") == "{Binding JpegQuality}" &&
                      slider.Parent?.Attribute("Visibility")?.Value.Contains("IsJpeg") == true);
        Assert.Contains(document.Descendants(presentation + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding ConvertCommand}");
    }
}

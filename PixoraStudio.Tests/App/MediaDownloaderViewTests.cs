using System.Xml.Linq;

namespace PixoraStudio.Tests.App;

public sealed class MediaDownloaderViewTests
{
    [Fact]
    public void MainWindow_ShouldRegisterMediaDownloaderNavigationAndTemplate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml");
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(document.Descendants(presentation + "Button"),
            button => (string?)button.Attribute("Content") == "⇩   Media Downloader" &&
                      (string?)button.Attribute("Command") == "{Binding OpenMediaDownloaderCommand}");

        Assert.Contains(document.Descendants(presentation + "DataTemplate"),
            template => (string?)template.Attribute("DataType") ==
                        "{x:Type vm:MediaDownloaderViewModel}");
    }

    [Fact]
    public void MediaDownloaderView_ShouldExposeUrlCheckOptionsAndQueue()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MediaDownloaderView.xaml");
        var document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Contains(document.Descendants(presentation + "TextBox"),
            textBox => ((string?)textBox.Attribute("Text"))?.Contains("{Binding Url}") == true);
        Assert.Contains(document.Descendants(presentation + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding CheckLinkCommand}");
        Assert.Contains(document.Descendants(presentation + "ComboBox"),
            comboBox => (string?)comboBox.Attribute("ItemsSource") == "{Binding FormatOptions}");
        Assert.Contains(document.Descendants(presentation + "ComboBox"),
            comboBox => (string?)comboBox.Attribute("ItemsSource") == "{Binding QualityOptions}");
        Assert.Contains(document.Descendants(presentation + "TextBlock"),
            text => (string?)text.Attribute("Text") == "DOWNLOAD QUEUE");
    }
}

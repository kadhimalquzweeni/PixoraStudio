using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixoraStudio.App.Views;

public sealed class AiUpscaleProgressWindow : Window
{
    private readonly TextBlock _statusText;
    private readonly ProgressBar _progressBar;

    public AiUpscaleProgressWindow()
    {
        Title = "Pixora AI Upscaling";
        Width = 360;
        Height = 132;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(37, 37, 37));
        Foreground = new SolidColorBrush(Color.FromRgb(238, 238, 238));
        ShowInTaskbar = false;

        _statusText = new TextBlock
        {
            Text = "Preparing Pixora AI…",
            Margin = new Thickness(18, 16, 18, 10),
            TextWrapping = TextWrapping.Wrap
        };
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 12,
            Margin = new Thickness(18, 0, 18, 18),
            IsIndeterminate = true
        };

        var panel = new StackPanel();
        panel.Children.Add(_statusText);
        panel.Children.Add(_progressBar);
        Content = panel;
    }

    public void UpdateProgress(PixoraStudio.Core.Interfaces.AiUpscaleProgress progress)
    {
        _statusText.Text = progress.Message;
        _progressBar.IsIndeterminate = progress.ProgressPercentage <= 0;
        _progressBar.Value = progress.ProgressPercentage;
    }
}

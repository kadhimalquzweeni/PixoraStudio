
using System.Windows;
using System.Windows.Input;

namespace PixoraStudio.App.Windows;

public partial class VideoOverlayWindow : Window
{
    public VideoOverlayWindow()
    {
        InitializeComponent();

        MouseEnter += Overlay_MouseEnter;
        MouseLeave += Overlay_MouseLeave;
    }

    private void Overlay_MouseEnter(
        object sender,
        MouseEventArgs e)
    {
        ShowControls();
    }

    private void Overlay_MouseLeave(
        object sender,
        MouseEventArgs e)
    {
        HideControls();
    }

    public void ShowControls()
    {
        PlayPauseButton.Visibility = Visibility.Visible;
        FullscreenButton.Visibility = Visibility.Visible;
    }

    public void HideControls()
    {
        PlayPauseButton.Visibility = Visibility.Collapsed;
        FullscreenButton.Visibility = Visibility.Collapsed;
    }

    public void SetFullscreenButton(bool isFullScreen)
    {
        FullscreenButton.ToolTip = isFullScreen
            ? "Exit Fullscreen"
            : "Enter Fullscreen";
    }

    private void FullscreenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? FullscreenRequested;
}
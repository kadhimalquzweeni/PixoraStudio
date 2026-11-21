
using PixoraStudio.App.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;

namespace PixoraStudio.App.Windows;

public partial class VideoOverlayWindow : Window
{
    private bool _isFullscreenMode;

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

        FullscreenControls.Visibility = _isFullscreenMode
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void HideControls()
    {
        PlayPauseButton.Visibility = Visibility.Collapsed;
        FullscreenButton.Visibility = Visibility.Collapsed;
        FullscreenControls.Visibility = Visibility.Collapsed;
    }

    public void SetFullscreenButton(bool isFullScreen)
    {
        _isFullscreenMode = isFullScreen;

        FullscreenButton.ToolTip = isFullScreen
            ? "Exit Fullscreen"
            : "Enter Fullscreen";

        FullscreenButton.Content = CreateIcon(
            (Geometry)FindResource(
                isFullScreen
                    ? "MinimizeIconGeometry"
                    : "MaximizeIconGeometry"),
            20);

        FullscreenControls.Visibility =
            isFullScreen && IsMouseOver
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private Path CreateIcon(Geometry geometry, double size)
    {
        return new Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
    }

    private void FullscreenButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        FullscreenRequested?.Invoke(this, EventArgs.Empty);
    }

    // Seek through the video when the user drags the slider.
    private void PlaybackSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded ||
            !PlaybackSlider.IsMouseCaptureWithin)
        {
            return;
        }

        if (DataContext is VideoEditorViewModel vm)
        {
            vm.SeekCommand.Execute(e.NewValue);
        }
    }

    // Change video volume.
    private void FullscreenVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded ||
            !VolumeSlider.IsMouseCaptureWithin)
        {
            return;
        }

        if (DataContext is VideoEditorViewModel vm)
        {
            vm.ChangeVolumeCommand.Execute(e.NewValue);
        }
    }

    // Set trim start to the current playback position.
    private void StartTrimHere_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not VideoEditorViewModel vm ||
            vm.TotalMilliseconds <= 0)
        {
            return;
        }

        vm.TrimStart = Math.Min(
            vm.PlaybackPosition,
            Math.Max(0, vm.TrimEnd - 1));
    }

    // Set trim end to the current playback position.
    private void EndTrimHere_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not VideoEditorViewModel vm ||
            vm.TotalMilliseconds <= 0)
        {
            return;
        }

        vm.TrimEnd = Math.Max(
            vm.PlaybackPosition,
            Math.Min(vm.TotalMilliseconds, vm.TrimStart + 1));
    }

    public event EventHandler? FullscreenRequested;
}

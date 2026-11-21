
using PixoraStudio.App.ViewModels;
using PixoraStudio.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    // A click on the playback track seeks immediately; clicking the thumb
    // remains a normal drag operation.
    private void PlaybackSlider_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!IsLoaded ||
            DataContext is not VideoEditorViewModel vm ||
            vm.TotalMilliseconds <= 0 ||
            !TryGetTrackClickValue(PlaybackSlider, e, out double value))
        {
            return;
        }

        PlaybackSlider.Value = value;
        vm.SeekCommand.Execute(value);
        e.Handled = true;
    }

    // Keep seeking continuously while the user drags the playback thumb.
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
            vm.SeekCommand.Execute(e.NewValue);
    }

    // A click on the volume track sets the level immediately; the thumb
    // remains draggable for fine adjustments.
    private void FullscreenVolumeSlider_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!IsLoaded ||
            DataContext is not VideoEditorViewModel vm ||
            !TryGetTrackClickValue(VolumeSlider, e, out double value))
        {
            return;
        }

        VolumeSlider.Value = value;
        vm.ChangeVolumeCommand.Execute(value);
        e.Handled = true;
    }

    // Keep volume changes responsive while the user drags the thumb.
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
            vm.ChangeVolumeCommand.Execute(e.NewValue);
    }

    private static bool TryGetTrackClickValue(
        Slider slider,
        MouseButtonEventArgs e,
        out double value)
    {
        slider.ApplyTemplate();
        slider.UpdateLayout();

        Track? track = FindVisualChild<Track>(slider);
        if (track is null)
        {
            value = slider.Value;
            return false;
        }

        // Let the Slider handle thumb clicks so mouse capture and dragging
        // continue to work as normal.
        if (track.Thumb?.IsMouseOver == true)
        {
            value = slider.Value;
            return false;
        }

        Point click = e.GetPosition(track);
        value = VideoTimelineMath.GetValueFromTrackClick(
            click.X,
            track.ActualWidth,
            track.Thumb?.ActualWidth ?? 0,
            slider.Minimum,
            slider.Maximum);

        return true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
                return match;

            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
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

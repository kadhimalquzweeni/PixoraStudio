using SkiaSharp;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixoraStudio.App.Views;

internal sealed record CanvasDialogResult(
    int Width,
    int Height,
    SKColor BackgroundColor,
    bool CentreContent);

/// <summary>Small modal dialog shared by New Canvas and Canvas Size.</summary>
internal sealed class CanvasDialogWindow : Window
{
    private const int MaximumDimension = 16_384;
    private const long MaximumPixelCount = 100_000_000;

    private readonly bool _isResize;
    private readonly TextBox _widthInput;
    private readonly TextBox _heightInput;
    private readonly ComboBox? _backgroundInput;
    private readonly TextBox? _customColourInput;
    private readonly CheckBox? _centreContentInput;
    private readonly CheckBox _lockAspectRatioInput;
    private readonly TextBlock _errorText;
    private readonly double _initialAspectRatio;
    private bool _isUpdatingDimensions;
    private CanvasDialogResult? _result;

    private CanvasDialogWindow(
        Window? owner,
        bool isResize,
        int width,
        int height)
    {
        _isResize = isResize;
        Owner = owner;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        Title = isResize ? "Canvas Size" : "New Canvas";
        Width = 370;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(37, 39, 46));
        Foreground = Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(22) };
        Content = panel;

        panel.Children.Add(new TextBlock
        {
            Text = isResize ? "Resize canvas" : "Create a new canvas",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        });

        panel.Children.Add(CreateLabel("Width (pixels)"));
        _widthInput = CreateInput(width);
        panel.Children.Add(_widthInput);

        panel.Children.Add(CreateLabel("Height (pixels)"));
        _heightInput = CreateInput(height);
        panel.Children.Add(_heightInput);
        _initialAspectRatio = width / (double)Math.Max(1, height);
        _widthInput.TextChanged += WidthInput_TextChanged;
        _heightInput.TextChanged += HeightInput_TextChanged;

        _lockAspectRatioInput = new CheckBox
        {
            Content = "Lock aspect ratio",
            IsChecked = false,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 10, 0, 4)
        };
        panel.Children.Add(_lockAspectRatioInput);

        if (isResize)
        {
            _centreContentInput = new CheckBox
            {
                Content = "Centre existing layers on the resized canvas",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 12, 0, 8)
            };
            panel.Children.Add(_centreContentInput);
        }
        else
        {
            panel.Children.Add(CreateLabel("Background"));
            _backgroundInput = new ComboBox
            {
                Margin = new Thickness(0, 3, 0, 8),
                Padding = new Thickness(6),
                ItemsSource = new[] { "Transparent", "White", "Black", "Custom colour" },
                SelectedIndex = 0
            };
            _backgroundInput.SelectionChanged += Background_SelectionChanged;
            panel.Children.Add(_backgroundInput);

            _customColourInput = CreateInput("#FFFFFF");
            _customColourInput.Visibility = Visibility.Collapsed;
            _customColourInput.ToolTip = "Enter a colour such as #RRGGBB or #AARRGGBB";
            panel.Children.Add(_customColourInput);
        }

        _errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(255, 125, 125)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 8)
        };
        panel.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Cancel",
            MinWidth = 82,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 7, 12, 7),
            IsCancel = true
        };
        cancel.Click += (_, _) => DialogResult = false;

        var confirm = new Button
        {
            Content = isResize ? "Resize" : "Create",
            MinWidth = 82,
            Padding = new Thickness(12, 7, 12, 7),
            Background = new SolidColorBrush(Color.FromRgb(8, 127, 196)),
            Foreground = Brushes.White,
            IsDefault = true
        };
        confirm.Click += Confirm_Click;

        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
    }

    public static CanvasDialogResult? ShowNewCanvas(Window? owner)
    {
        var dialog = new CanvasDialogWindow(owner, false, 1920, 1080);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    public static CanvasDialogResult? ShowResizeCanvas(Window? owner, int width, int height)
    {
        var dialog = new CanvasDialogWindow(owner, true, width, height);
        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    private static TextBlock CreateLabel(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(205, 208, 216)),
        Margin = new Thickness(0, 8, 0, 2)
    };

    private static TextBox CreateInput(object value) => new()
    {
        Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        Padding = new Thickness(8, 6, 8, 6),
        Background = new SolidColorBrush(Color.FromRgb(28, 30, 36)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(72, 76, 86))
    };

    private void WidthInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingDimensions || _lockAspectRatioInput.IsChecked != true ||
            !int.TryParse(_widthInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            width <= 0)
            return;

        try
        {
            _isUpdatingDimensions = true;
            _heightInput.Text = Math.Max(1, (int)Math.Round(width / _initialAspectRatio))
                .ToString(CultureInfo.InvariantCulture);
        }
        finally { _isUpdatingDimensions = false; }
    }

    private void HeightInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingDimensions || _lockAspectRatioInput.IsChecked != true ||
            !int.TryParse(_heightInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
            height <= 0)
            return;

        try
        {
            _isUpdatingDimensions = true;
            _widthInput.Text = Math.Max(1, (int)Math.Round(height * _initialAspectRatio))
                .ToString(CultureInfo.InvariantCulture);
        }
        finally { _isUpdatingDimensions = false; }
    }
    private void Background_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_customColourInput is not null && _backgroundInput is not null)
        {
            _customColourInput.Visibility = _backgroundInput.SelectedIndex == 3
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static bool TryParseHexColour(string? value, out SKColor colour)
    {
        colour = SKColors.Transparent;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length == 6 &&
            byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) &&
            byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) &&
            byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
        {
            colour = new SKColor(red, green, blue);
            return true;
        }

        if (hex.Length == 8 &&
            byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte alpha) &&
            byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) &&
            byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) &&
            byte.TryParse(hex.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
        {
            colour = new SKColor(red, green, blue, alpha);
            return true;
        }

        return false;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(_widthInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(_heightInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
            width <= 0 || height <= 0 ||
            width > MaximumDimension || height > MaximumDimension ||
            (long)width * height > MaximumPixelCount)
        {
            _errorText.Text =
                $"Enter valid dimensions. Each side must be 1–{MaximumDimension:N0} px and the canvas must not exceed {MaximumPixelCount:N0} pixels.";
            return;
        }

        SKColor background = SKColors.Transparent;
        if (!_isResize)
        {
            switch (_backgroundInput?.SelectedIndex)
            {
                case 1:
                    background = SKColors.White;
                    break;
                case 2:
                    background = SKColors.Black;
                    break;
                case 3:
                    if (!TryParseHexColour(_customColourInput?.Text, out background))
                    {
                        _errorText.Text = "Enter a valid colour, for example #4A90E2 or #804A90E2.";
                        return;
                    }
                    break;
            }
        }

        _result = new CanvasDialogResult(
            width,
            height,
            background,
            _centreContentInput?.IsChecked == true);
        DialogResult = true;
    }
}

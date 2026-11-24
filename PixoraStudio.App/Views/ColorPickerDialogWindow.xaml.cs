using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixoraStudio.App.Views;

public partial class ColorPickerDialogWindow : Window
{
    public string SelectedColor { get; private set; } = "#FFFFFFFF";

    public ColorPickerDialogWindow()
    {
        InitializeComponent();
        HexColorTextBox.TextChanged += HexColorTextBox_TextChanged;
        HexColorTextBox.Text = SelectedColor;
        HexColorTextBox.SelectAll();
    }

    public ColorPickerDialogWindow(string initialColor) : this()
    {
        if (!string.IsNullOrWhiteSpace(initialColor))
            HexColorTextBox.Text = initialColor;
        HexColorTextBox.SelectAll();
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string color })
            HexColorTextBox.Text = color;
    }

    private void HexColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TryParseColor(HexColorTextBox.Text, out var color))
        {
            ColorPreviewBorder.Background = new SolidColorBrush(color);
            ValidationTextBlock.Text = string.Empty;
            ApplyButton.IsEnabled = true;
        }
        else
        {
            ValidationTextBlock.Text = "Enter a valid colour, such as #FF4A90E2.";
            ApplyButton.IsEnabled = false;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseColor(HexColorTextBox.Text, out var color))
            return;

        SelectedColor = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        DialogResult = true;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            if (new ColorConverter().ConvertFromString(value.Trim()) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Invalid custom hex input is reported beside the text box.
        }

        return false;
    }
}

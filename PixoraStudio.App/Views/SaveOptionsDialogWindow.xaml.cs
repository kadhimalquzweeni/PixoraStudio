using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixoraStudio.Core.Interfaces;

namespace PixoraStudio.App.Views;

public enum SaveImageFormat
{
    Jpg,
    Png,
    WebP,
    Bmp,
    Icon
}

public partial class SaveOptionsDialogWindow : Window
{
    private readonly int _sourceWidth;
    private readonly int _sourceHeight;
    private readonly double _aspectRatio;
    private readonly bool _hasTransparency;
    private readonly bool _canUsePixoraAi;
    private bool _isUpdatingDimensions;
    private bool _isInitialized;
    private SaveImageFormat _selectedFormat = SaveImageFormat.Jpg;

    public SaveImageFormat SelectedFormat => _selectedFormat;
    public bool UsePixoraAiUpscaling => _canUsePixoraAi && PixoraAiUpscaleCheckBox.IsChecked == true;
    public AiUpscaleModel SelectedPixoraAiModel =>
        PixoraAiModelComboBox.SelectedItem is ComboBoxItem { Tag: AiUpscaleModel model }
            ? model
            : AiUpscaleModel.Photo;
    public int OutputWidth => int.Parse(WidthTextBox.Text);
    public int OutputHeight => int.Parse(HeightTextBox.Text);
    public int Quality => (int)Math.Round(QualitySlider.Value);
    public string OutputFolder => FolderPathTextBox.Text.Trim();
    public string OutputFileName => NormalizeFileName(FileNameTextBox.Text);
    public int[] SelectedIconSizes =>
    [
        Icon16CheckBox.IsChecked == true ? 16 : 0,
        Icon24CheckBox.IsChecked == true ? 24 : 0,
        Icon32CheckBox.IsChecked == true ? 32 : 0,
        Icon48CheckBox.IsChecked == true ? 48 : 0,
        Icon64CheckBox.IsChecked == true ? 64 : 0,
        Icon128CheckBox.IsChecked == true ? 128 : 0,
        Icon256CheckBox.IsChecked == true ? 256 : 0
    ];

    public SaveOptionsDialogWindow(BitmapSource preview, int width, int height,
        string initialFolder, string initialName, bool hasTransparency,
        bool canUsePhotoModel = false, bool canUseSpriteModel = false)
    {
        InitializeComponent();
        _hasTransparency = hasTransparency;
        _canUsePixoraAi = canUsePhotoModel || canUseSpriteModel;
        if (canUsePhotoModel)
            PixoraAiModelComboBox.Items.Add(new ComboBoxItem { Content = "Photo model", Tag = AiUpscaleModel.Photo });
        if (canUseSpriteModel)
            PixoraAiModelComboBox.Items.Add(new ComboBoxItem { Content = "Sprite / emoji model", Tag = AiUpscaleModel.Sprite });
        if (PixoraAiModelComboBox.Items.Count > 0)
            PixoraAiModelComboBox.SelectedIndex = 0;
        PixoraAiUpscaleCheckBox.IsEnabled = _canUsePixoraAi;
        PixoraAiUpscaleHint.Text = _canUsePixoraAi
            ? "Runs locally with the selected model. 4x results are limited to 32 megapixels."
            : "Train a local Pixora model first to enable AI upscaling.";
        if (_hasTransparency)
            _selectedFormat = SaveImageFormat.Png;
        _sourceWidth = Math.Max(1, width);
        _sourceHeight = Math.Max(1, height);
        _aspectRatio = (double)_sourceWidth / _sourceHeight;

        PreviewImage.Source = preview;
        FileNameTextBox.Text = initialName;
        FolderPathTextBox.Text = Directory.Exists(initialFolder)
            ? initialFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!Directory.Exists(FolderPathTextBox.Text))
            FolderPathTextBox.Text = Environment.CurrentDirectory;
        _isUpdatingDimensions = true;
        WidthTextBox.Text = _sourceWidth.ToString();
        HeightTextBox.Text = _sourceHeight.ToString();
        _isUpdatingDimensions = false;
        OneXScaleRadioButton.IsEnabled = FitsOutputLimit(1);
        TwoXScaleRadioButton.IsEnabled = FitsOutputLimit(2);
        FourXScaleRadioButton.IsEnabled = FitsOutputLimit(4);
        if (OneXScaleRadioButton.IsEnabled)
            OneXScaleRadioButton.IsChecked = true;
        else
            CustomScaleRadioButton.IsChecked = true;
        UpdateFormatSelection();
        _isInitialized = true;
        UpdatePreviewDescription();
    }

    private void FormatCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string tag })
            return;

        _selectedFormat = tag switch
        {
            "Png" => SaveImageFormat.Png,
            "WebP" => SaveImageFormat.WebP,
            "Bmp" => SaveImageFormat.Bmp,
            "Icon" => SaveImageFormat.Icon,
            _ => SaveImageFormat.Jpg
        };
        UpdateFormatSelection();
        UpdatePreviewDescription();
        e.Handled = true;
    }

    private void UpdateFormatSelection()
    {
        SetCardState(JpgCard, _selectedFormat == SaveImageFormat.Jpg);
        SetCardState(PngCard, _selectedFormat == SaveImageFormat.Png);
        SetCardState(WebpCard, _selectedFormat == SaveImageFormat.WebP);
        SetCardState(BmpCard, _selectedFormat == SaveImageFormat.Bmp);
        SetCardState(IconCard, _selectedFormat == SaveImageFormat.Icon);
        JpgCard.IsEnabled = !_hasTransparency;
        BmpCard.IsEnabled = !_hasTransparency;
        PixoraAiUpscaleCheckBox.IsEnabled = _canUsePixoraAi && _selectedFormat != SaveImageFormat.Icon;
        if (_selectedFormat == SaveImageFormat.Icon)
            PixoraAiUpscaleCheckBox.IsChecked = false;
        JpgCard.Opacity = _hasTransparency ? 0.45 : 1;
        BmpCard.Opacity = _hasTransparency ? 0.45 : 1;
        TransparencyHint.Visibility = _hasTransparency ? Visibility.Visible : Visibility.Collapsed;
        PreviewBackground.Background = _selectedFormat == SaveImageFormat.Jpg
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(8, 8, 8));

        bool usesQuality = _selectedFormat is SaveImageFormat.Jpg or SaveImageFormat.WebP;
        QualityPanel.Visibility = usesQuality ? Visibility.Visible : Visibility.Collapsed;
        DimensionsPanel.Visibility = _selectedFormat == SaveImageFormat.Icon
            ? Visibility.Collapsed
            : Visibility.Visible;
        IconSizesPanel.Visibility = _selectedFormat == SaveImageFormat.Icon
            ? Visibility.Visible
            : Visibility.Collapsed;
        FileExtensionText.Text = GetExtension(_selectedFormat);
        UpdateAiModelSelectorState();
    }

    private void UpdateAiModelSelectorState()
    {
        PixoraAiModelComboBox.IsEnabled =
            _canUsePixoraAi && PixoraAiUpscaleCheckBox.IsEnabled &&
            PixoraAiUpscaleCheckBox.IsChecked == true;
    }

    private static void SetCardState(Border card, bool isSelected)
    {
        card.Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                isSelected ? "#063E52" : "#343434"));
        card.BorderBrush = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                isSelected ? "#079BD8" : "#454545"));
    }

    private static string GetExtension(SaveImageFormat format) => format switch
    {
        SaveImageFormat.Jpg => ".jpg",
        SaveImageFormat.Png => ".png",
        SaveImageFormat.WebP => ".webp",
        SaveImageFormat.Bmp => ".bmp",
        SaveImageFormat.Icon => ".ico",
        _ => ".jpg"
    };

    private void UpdatePreviewDescription()
    {
        PreviewFormatText.Text = $"Format: {GetExtension(_selectedFormat).TrimStart('.').ToUpperInvariant()}";
        PreviewDimensionsText.Text = _selectedFormat == SaveImageFormat.Icon
            ? "Multiple icon sizes"
            : $"{WidthTextBox.Text} × {HeightTextBox.Text} px";
    }

    private void QualitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityValueText is not null)
            QualityValueText.Text = $"{(int)Math.Round(e.NewValue)}%";
    }

    private void QualityPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } && double.TryParse(value, out double quality))
            QualitySlider.Value = quality;
    }

    private void ScalePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string preset } ||
            !int.TryParse(preset, out int multiplier))
        {
            PixoraAiUpscaleCheckBox.IsChecked = false;
            UpdateAiModelSelectorState();
            UpdatePreviewDescription();
            return;
        }

        if (!FitsOutputLimit(multiplier))
        {
            CustomScaleRadioButton.IsChecked = true;
            PixoraAiUpscaleCheckBox.IsChecked = false;
            UpdateAiModelSelectorState();
            return;
        }

        if (multiplier != 4)
            PixoraAiUpscaleCheckBox.IsChecked = false;
        UpdateAiModelSelectorState();
        _isUpdatingDimensions = true;
        WidthTextBox.Text = (_sourceWidth * multiplier).ToString();
        HeightTextBox.Text = (_sourceHeight * multiplier).ToString();
        _isUpdatingDimensions = false;
        UpdatePreviewDescription();
    }

    private bool FitsOutputLimit(int multiplier) =>
        _sourceWidth <= 16384 / multiplier &&
        _sourceHeight <= 16384 / multiplier;

    private void PixoraAiUpscale_Click(object sender, RoutedEventArgs e)
    {
        if (!_canUsePixoraAi || PixoraAiUpscaleCheckBox.IsChecked != true)
        {
            UpdateAiModelSelectorState();
            return;
        }

        if (!FitsOutputLimit(4))
        {
            PixoraAiUpscaleCheckBox.IsChecked = false;
            UpdateAiModelSelectorState();
            ShowValidation("Pixora AI's 4x output would exceed the 16,384 pixel dimension limit.");
            return;
        }

        FourXScaleRadioButton.IsChecked = true;
        _isUpdatingDimensions = true;
        WidthTextBox.Text = (_sourceWidth * 4).ToString();
        HeightTextBox.Text = (_sourceHeight * 4).ToString();
        _isUpdatingDimensions = false;
        UpdateAiModelSelectorState();
        UpdatePreviewDescription();
    }

    private void DimensionsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _isUpdatingDimensions)
            return;

        CustomScaleRadioButton.IsChecked = true;
        PixoraAiUpscaleCheckBox.IsChecked = false;
        UpdateAiModelSelectorState();
        UpdatePreviewDescription();
    }

    private void WidthTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingDimensions || LockAspectRatioCheckBox.IsChecked != true ||
            !int.TryParse(WidthTextBox.Text, out int width) || width <= 0)
            return;

        _isUpdatingDimensions = true;
        HeightTextBox.Text = Math.Max(1, (int)Math.Round(width / _aspectRatio)).ToString();
        _isUpdatingDimensions = false;
        UpdatePreviewDescription();
    }

    private void HeightTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingDimensions || LockAspectRatioCheckBox.IsChecked != true ||
            !int.TryParse(HeightTextBox.Text, out int height) || height <= 0)
            return;

        _isUpdatingDimensions = true;
        WidthTextBox.Text = Math.Max(1, (int)Math.Round(height * _aspectRatio)).ToString();
        _isUpdatingDimensions = false;
        UpdatePreviewDescription();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where to save",
            InitialDirectory = Directory.Exists(OutputFolder)
                ? OutputFolder
                : Environment.CurrentDirectory,
            Multiselect = false
        };
        if (folderDialog.ShowDialog(this) == true)
            FolderPathTextBox.Text = folderDialog.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationText.Visibility = Visibility.Collapsed;
        string name = NormalizeFileName(FileNameTextBox.Text);
        if (string.IsNullOrWhiteSpace(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowValidation("Enter a valid file name.");
            return;
        }

        if (!Directory.Exists(OutputFolder))
        {
            ShowValidation("Choose an existing folder to save to.");
            return;
        }

        if (_selectedFormat == SaveImageFormat.Icon && !SelectedIconSizes.Any(size => size > 0))
        {
            ShowValidation("Select at least one icon size.");
            return;
        }

        if (_selectedFormat != SaveImageFormat.Icon &&
            (!int.TryParse(WidthTextBox.Text, out int width) ||
             !int.TryParse(HeightTextBox.Text, out int height) ||
             width < 1 || height < 1 || width > 16384 || height > 16384))
        {
            ShowValidation("Width and height must be between 1 and 16,384 pixels.");
            return;
        }

        FileNameTextBox.Text = name;
        DialogResult = true;
    }

    private static string NormalizeFileName(string? value)
    {
        string name = (value ?? string.Empty).Trim();
        string extension = Path.GetExtension(name);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(name);
        return name;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

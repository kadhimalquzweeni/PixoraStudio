using PixoraStudio.Core.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PixoraStudio.App.Views;

public sealed class AddLayerDialogWindow : Window
{
    public PhotoLayerContentType? SelectedType { get; private set; }

    private AddLayerDialogWindow()
    {
        Title = "Add Layer"; Width = 500; Height = 266;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(38,38,38));
        Foreground = Brushes.White;
        var root = new DockPanel();
        var header = new Border { Background = new SolidColorBrush(Color.FromRgb(34,34,34)), Padding = new Thickness(14,10,14,10) };
        header.Child = new TextBlock { Text = "Add Layer", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(20,18,20,16) };
        body.Children.Add(new TextBlock { Text = "Select what type of layer to add", Margin = new Thickness(0,0,0,20) });
        var choices = new UniformGrid { Columns = 5 };
        AddChoice(choices, "□", "Empty", PhotoLayerContentType.Empty);
        AddChoice(choices, "▧", "Image", PhotoLayerContentType.Image);
        AddChoice(choices, "▣", "Frame", PhotoLayerContentType.Frame);
        AddChoice(choices, "T", "Text", PhotoLayerContentType.Text);
        AddChoice(choices, "◆", "Shape", PhotoLayerContentType.Shape);
        body.Children.Add(choices);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,28,0,0) };
        var cancel = new Button { Content = "Cancel", Width = 110, Padding = new Thickness(12,7,12,7), Background = Brushes.Transparent, Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(150,150,150)) };
        cancel.Click += (_,_) => { SelectedType = null; DialogResult = false; };
        footer.Children.Add(cancel); body.Children.Add(footer);
        root.Children.Add(body); Content = root;
    }

    private void AddChoice(Panel panel, string glyph, string label, PhotoLayerContentType type)
    {
        var button = new Button { Margin = new Thickness(3), Padding = new Thickness(5), Height = 80, Background = new SolidColorBrush(Color.FromRgb(53,53,53)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = glyph, FontSize = 27, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = label, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,3,0,0) });
        button.Content = stack;
        button.Click += (_,_) => { SelectedType = type; DialogResult = true; };
        panel.Children.Add(button);
    }

    public static PhotoLayerContentType? Show(Window? owner)
    {
        var dialog = new AddLayerDialogWindow();
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.SelectedType : null;
    }
}

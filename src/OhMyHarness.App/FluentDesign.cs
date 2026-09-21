using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OhMyHarness.App;

internal static class FluentDesign
{
    public static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
    public static Brush Card => Resource("CardBackgroundFillColorDefaultBrush");
    public static Brush Stroke => Resource("CardStrokeColorDefaultBrush");
    public static Brush Primary => Resource("TextFillColorPrimaryBrush");
    public static Brush Secondary => Resource("TextFillColorSecondaryBrush");

    public static Border Surface(UIElement child, double padding = 20) => new()
    {
        Child = child, Background = Card, BorderBrush = Stroke, BorderThickness = new(1),
        CornerRadius = new(8), Padding = new(padding), HorizontalAlignment = HorizontalAlignment.Stretch
    };

    public static Border Setting(string title, string description, Control control, FrameworkElement? details = null)
    {
        AutomationProperties.SetName(control, title);
        var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 14, Foreground = Primary,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description))
            copy.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Secondary, TextWrapping = TextWrapping.Wrap });
        var row = new Grid { ColumnSpacing = 24, RowSpacing = 0 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.Children.Add(copy);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1); row.Children.Add(control);
        if (details != null)
        { Grid.SetRow(details, 1); Grid.SetColumnSpan(details, 2); row.Children.Add(details); }
        row.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 420;
            Grid.SetColumnSpan(copy, narrow ? 2 : 1);
            Grid.SetColumn(control, narrow ? 0 : 1); Grid.SetRow(control, narrow ? 1 : 0);
            control.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(0);
            if (details != null) Grid.SetRow(details, narrow ? 2 : 1);
        };
        return Surface(row, 16);
    }

    public static FontIcon Icon(string glyph, double size = 16) => new()
    { Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size };

    public static void IconButton(Button button, string glyph, string label, bool showLabel = true)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(Icon(glyph));
        if (showLabel) content.Children.Add(new TextBlock { Text = label, Tag = label, VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        button.Tag = null;
        AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label);
    }

    public static void WindowChrome(Window window)
    {
        window.SystemBackdrop = new MicaBackdrop();
        var bar = window.AppWindow.TitleBar;
        bar.BackgroundColor = ColorHelper.FromArgb(255, 32, 32, 32);
        bar.ForegroundColor = Colors.White;
        bar.InactiveBackgroundColor = ColorHelper.FromArgb(255, 32, 32, 32);
        bar.InactiveForegroundColor = ColorHelper.FromArgb(255, 160, 160, 160);
        bar.ButtonBackgroundColor = ColorHelper.FromArgb(255, 32, 32, 32); bar.ButtonForegroundColor = Colors.White;
        bar.ButtonInactiveBackgroundColor = ColorHelper.FromArgb(255, 32, 32, 32);
        bar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 160, 160, 160);
    }
}

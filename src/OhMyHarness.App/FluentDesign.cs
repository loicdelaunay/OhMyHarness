using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

internal static class FluentDesign
{
    static AppearanceTheme theme = AppearanceThemes.All[0];
    static readonly Dictionary<string, SolidColorBrush> resources = [];
    static readonly Dictionary<(byte R, byte G, byte B), SolidColorBrush> adapted = [];
    static Windows.UI.Color Color(string hex) => ColorHelper.FromArgb(255, Convert.ToByte(hex.Substring(1,2),16), Convert.ToByte(hex.Substring(3,2),16), Convert.ToByte(hex.Substring(5,2),16));
    static Windows.UI.Color ResourceColor(string key) => key switch
    {
        "TextFillColorPrimaryBrush" => Color(theme.Text),
        "TextFillColorSecondaryBrush" => Color(theme.Muted),
        "CardStrokeColorDefaultBrush" => ColorHelper.FromArgb(theme.Dark ? (byte)24 : (byte)32, theme.Dark ? (byte)255 : (byte)0, theme.Dark ? (byte)255 : (byte)0, theme.Dark ? (byte)255 : (byte)0),
        "ConversationHoverFillBrush" => Blend(Color(theme.Surface), Color(theme.Dark ? theme.Text : theme.Accent), theme.Dark ? .09 : .08),
        "ConversationHoverStrokeBrush" => WithOpacity(Color(theme.Dark ? theme.Text : theme.Accent), theme.Dark ? (byte)80 : (byte)120),
        "UserMessageFillBrush" => Blend(Color(theme.Surface), Color(theme.Accent), theme.Dark ? .20 : .10),
        "UserMessageStrokeBrush" => Blend(Color(theme.Surface), Color(theme.Accent), theme.Dark ? .45 : .30),
        "AssistantMessageFillBrush" => Blend(Color(theme.Surface), Color(theme.Text), theme.Dark ? .055 : .025),
        "AssistantMessageStrokeBrush" => Blend(Color(theme.Surface), Color(theme.Text), theme.Dark ? .17 : .14),
        "ToolMessageFillBrush" => Blend(Color(theme.Surface), Color(theme.Dark ? "#A78BFA" : "#7452AD"), theme.Dark ? .14 : .075),
        "ToolMessageStrokeBrush" => Blend(Color(theme.Surface), Color(theme.Dark ? "#A78BFA" : "#7452AD"), theme.Dark ? .35 : .23),
        "ToolMessageTitleBrush" => Color(theme.Dark ? "#D2BFFF" : "#603F91"),
        "ToolMessageErrorStrokeBrush" => Color(theme.Dark ? "#E58C88" : "#B5413C"),
        "SolidBackgroundFillColorBaseBrush" => Color(theme.Background),
        "SystemAccentColor" => Color(theme.Accent),
        "AccentFillColorDefaultBrush" or "AccentTextFillColorPrimaryBrush" or "AccentTextFillColorSecondaryBrush" or "AccentTextFillColorTertiaryBrush" => Color(theme.Accent),
        "AccentFillColorSecondaryBrush" => WithOpacity(Color(theme.Accent), 230),
        "AccentFillColorTertiaryBrush" => WithOpacity(Color(theme.Accent), 204),
        "TextOnAccentFillColorPrimaryBrush" => AccentForeground(),
        "TextOnAccentFillColorSecondaryBrush" => WithOpacity(AccentForeground(), 200),
        _ => Color(theme.Surface)
    };
    static Windows.UI.Color AccentForeground()
    {
        var accent = Color(theme.Accent);
        static double Channel(byte value)
        {
            double normalized = value / 255d;
            return normalized <= .04045 ? normalized / 12.92 : Math.Pow((normalized + .055) / 1.055, 2.4);
        }
        double luminance = .2126 * Channel(accent.R) + .7152 * Channel(accent.G) + .0722 * Channel(accent.B);
        return Color(luminance > .179 && theme.Dark ? theme.Background : "#FFFFFF");
    }
    static Windows.UI.Color WithOpacity(Windows.UI.Color color, byte alpha) => ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
    static Windows.UI.Color Blend(Windows.UI.Color background, Windows.UI.Color foreground, double amount) =>
        ColorHelper.FromArgb(255,
            (byte)Math.Round(background.R * (1 - amount) + foreground.R * amount),
            (byte)Math.Round(background.G * (1 - amount) + foreground.G * amount),
            (byte)Math.Round(background.B * (1 - amount) + foreground.B * amount));
    public static Brush Resource(string key)
    {
        if (!resources.TryGetValue(key, out var brush)) resources[key] = brush = new(ResourceColor(key));
        return brush;
    }
    public static SolidColorBrush Adapt(byte r, byte g, byte b)
    {
        if (!adapted.TryGetValue((r,g,b), out var brush)) adapted[(r,g,b)] = brush = new(AdaptColor(r,g,b));
        return brush;
    }
    static Windows.UI.Color AdaptColor(byte r, byte g, byte b)
    {
        if (Math.Max(r,Math.Max(g,b)) < 115) return Color(theme.Surface);
        if (Math.Max(r,Math.Max(g,b)) - Math.Min(r,Math.Min(g,b)) < 85 && Math.Min(r,Math.Min(g,b)) > 105)
            return Color(Math.Max(r,Math.Max(g,b)) > 220 ? theme.Text : theme.Muted);
        if (!theme.Dark) return ColorHelper.FromArgb(255, (byte)(r*.55), (byte)(g*.55), (byte)(b*.55));
        return ColorHelper.FromArgb(255,r,g,b);
    }
    public static void SetTheme(string id)
    {
        theme = AppearanceThemes.Get(id);
        foreach (var (key,brush) in resources) brush.Color = ResourceColor(key);
        foreach (var (key,brush) in adapted) brush.Color = AdaptColor(key.R,key.G,key.B);
        // Install shared brushes before controls are built, then mutate them on theme changes.
        foreach (var key in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
            "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
            "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush" })
            Application.Current.Resources[key] = Resource(key);
        Application.Current.Resources["SystemAccentColor"] = Color(theme.Accent);
    }
    public static Brush Card => Resource("CardBackgroundFillColorDefaultBrush");
    public static Brush Stroke => Resource("CardStrokeColorDefaultBrush");
    public static Brush Primary => Resource("TextFillColorPrimaryBrush");
    public static Brush Secondary => Resource("TextFillColorSecondaryBrush");

    public static Border MessageSurface(UIElement? content, string role, bool error = false)
    {
        var prefix = role switch { "user" => "User", "tool" => "Tool", _ => "Assistant" };
        return new Border
        {
            Child = content, Background = Resource(prefix + "MessageFillBrush"),
            BorderBrush = Resource(error ? "ToolMessageErrorStrokeBrush" : prefix + "MessageStrokeBrush"),
            BorderThickness = new(1), CornerRadius = new(12), Padding = new(16),
            Margin = new(0), HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

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
#if WINDOWS
        window.SystemBackdrop = new MicaBackdrop();
        var bar = window.AppWindow.TitleBar;
        bar.BackgroundColor = bar.InactiveBackgroundColor = Color(theme.Background);
        bar.ForegroundColor = bar.ButtonForegroundColor = Color(theme.Text);
        bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = Color(theme.Muted);
        bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = Color(theme.Background);
#endif
    }
}

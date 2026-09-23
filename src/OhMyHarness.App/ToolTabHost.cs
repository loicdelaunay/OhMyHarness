using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OhMyHarness.App;

/// <summary>Only the selected tool page is kept visible, including its native WebView.</summary>
internal sealed class ToolTabHost : Grid
{
    readonly StackPanel headers = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    readonly List<(TextBlock Label, Border Indicator, FrameworkElement Page)> tabs = [];
    int selectedIndex;

    public event EventHandler? SelectionChanged;
    public int Count => tabs.Count;
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0 || value >= tabs.Count || value == selectedIndex) return;
            selectedIndex = value;
            UpdateSelection();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ToolTabHost()
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var headerScroll = new ScrollViewer { Content = headers,
            HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Children.Add(headerScroll);
    }

    public void AddTab(string title, FrameworkElement page)
    {
        var index = tabs.Count;
        var label = new TextBlock { Text = title, FontSize = 15 };
        var indicator = new Border { Height = 2, CornerRadius = new(1), Background = FluentDesign.Resource("AccentFillColorDefaultBrush") };
        var headerContent = new StackPanel { Spacing = 3 };
        headerContent.Children.Add(label);
        headerContent.Children.Add(indicator);
        var button = new Button { Content = headerContent, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new(0), Padding = new(10, 6, 10, 3), MinWidth = 0 };
        button.Click += (_, _) => SelectedIndex = index;
        headers.Children.Add(button);
        SetRow(page, 1);
        Children.Add(page);
        tabs.Add((label, indicator, page));
        UpdateSelection();
    }

    public void SetTitle(int index, string title)
    {
        if ((uint)index < tabs.Count) tabs[index].Label.Text = title;
    }

    void UpdateSelection()
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            tabs[i].Page.Visibility = i == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
            tabs[i].Label.Foreground = i == selectedIndex ? FluentDesign.Primary : FluentDesign.Secondary;
            tabs[i].Indicator.Visibility = i == selectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

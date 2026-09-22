using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    bool composerInfoExpanded = true;
    readonly Button infoToggle = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 28, Padding = new(12,4,12,4) };
    void ApplyAppearance()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        TextZoom.Set(config.FontZoomPercent);
        FluentDesign.SetTheme(config.Theme);
        ApplyBranding();
        root.RequestedTheme = AppearanceThemes.Get(config.Theme).Dark ? ElementTheme.Dark : ElementTheme.Light;
        root.Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        shell.PaneBackground = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        FluentDesign.WindowChrome(this);
        if (settingsWindow?.Content is FrameworkElement settingsRoot)
        { settingsRoot.RequestedTheme = root.RequestedTheme; FluentDesign.WindowChrome(settingsWindow); }
        if (tasksWindow?.Content is FrameworkElement tasksRoot)
        { tasksRoot.RequestedTheme = root.RequestedTheme; FluentDesign.WindowChrome(tasksWindow); }
        composerInfoExpanded = config.ComposerInfoExpanded;
        UpdateInfoPanel();
    }
    void UpdateInfoPanel()
    {
        bool expanded = composerInfoExpanded;
        floatingInfoBar.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        infoToggle.Content = (expanded ? "⌄  " : "›  ") + WorkflowText("Modèle, débit et contexte", "Model, speed and context");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(infoToggle, WorkflowText("Afficher les informations du modèle", "Show model information"));
    }
    UIElement BuildComposerSurface()
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(infoToggle);
        panel.Children.Add(BuildFloatingInfoBar());
        floatingInfoBar.CornerRadius = new(0); floatingInfoBar.BorderThickness = new(0);
        panel.Children.Add(BuildComposer());
        infoToggle.Click += async (_, _) => await Guard(async () =>
        {
            composerInfoExpanded = !composerInfoExpanded;
            UpdateInfoPanel();
            var config = FeatureSettings.Read(state.FeaturesJson); config.ComposerInfoExpanded = composerInfoExpanded;
            state.FeaturesJson = config.Json(); await db.SaveChangesAsync();
        });
        var surface = FluentDesign.Surface(panel, 0);
        UpdateInfoPanel();
        return surface;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeThemes(string output)
    {
        var body = new StackPanel { Spacing = 16 };
        var title = new TextBlock { FontSize = 24, Foreground = FluentDesign.Primary };
        body.Children.Add(title);
        var entry = new TextBox { Text = "Texte sélectionné : contraste lisible", Header = "Saisie et sélection", FontSize = 16 };
        body.Children.Add(entry);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(new Button { Content = "Action principale", Style = (Style)Application.Current.Resources["AccentButtonStyle"] });
        actions.Children.Add(new Button { Content = "Action secondaire" });
        actions.Children.Add(new CheckBox { Content = "Activé", IsChecked = true });
        actions.Children.Add(new ToggleSwitch { IsOn = true });
        body.Children.Add(actions);
        var model = new ComboBox { Header = "Modèle", ItemsSource = new[] { "DeepSeek", "OpenAI", "OpenCode" }, SelectedIndex = 0, MinWidth = 240 };
        body.Children.Add(model);
        var list = new ListView { ItemsSource = new[] { "Élément sélectionné", "Élément disponible" }, SelectedIndex = 0, Height = 90 };
        body.Children.Add(list);
        var sample = new StackPanel { Spacing = 8 };
        foreach (var role in new[] { "user", "assistant", "tool" })
            sample.Children.Add(FluentDesign.MessageSurface(new TextBlock { Text = role + " · Message et détails lisibles", Foreground = FluentDesign.Primary }, role));
        body.Children.Add(sample);
        body.Children.Add(new TextBlock { Text = "Lien / accent accessible", Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush") });
        var presenter = new MenuFlyoutPresenter { ItemsSource = new MenuFlyoutItemBase[] { new MenuFlyoutItem { Text = "Modifier", Icon = new SymbolIcon(Symbol.Edit) }, new MenuFlyoutItem { Text = "Supprimer", Icon = new SymbolIcon(Symbol.Delete) } } };
        body.Children.Add(presenter);
        var overlay = new Border { Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Padding = new(24), Child = body };
        root.Children.Add(overlay);
        await Task.Delay(750);
        root.UpdateLayout();
        foreach (var palette in AppearanceThemes.All)
        {
            ApplyTheme(palette.Id); title.Text = palette.French;
            entry.Focus(FocusState.Programmatic); entry.SelectAll();
            await Task.Delay(300);
            static string Hex(Brush brush) { var c = ((SolidColorBrush)brush).Color; return $"#{c.R:X2}{c.G:X2}{c.B:X2}"; }
            void Contrast(string foreground, string background)
            {
                var ratio = ThemeContrast.Ratio(Hex(FluentDesign.Resource(foreground)), Hex(FluentDesign.Resource(background)));
                if (ratio < 4.5) throw new Exception($"{palette.Id}: {foreground}/{background} = {ratio:0.00}");
            }
            Contrast("TextFillColorPrimaryBrush", "CardBackgroundFillColorDefaultBrush");
            Contrast("TextFillColorSecondaryBrush", "CardBackgroundFillColorDefaultBrush");
            Contrast("AccentButtonForeground", "AccentButtonBackground");
            Contrast("TextOnAccentFillColorSelectedTextBrush", "TextControlSelectionHighlightColor");
            Contrast("MenuFlyoutItemForeground", "MenuFlyoutPresenterBackground");
            Contrast("ComboBoxItemForegroundSelected", "ComboBoxItemBackgroundSelected");
            Contrast("ListViewItemForegroundSelected", "ListViewItemBackgroundSelectedPointerOver");
            Contrast("AccentTextFillColorPrimaryBrush", "CardBackgroundFillColorDefaultBrush");
            if (ThemeContrast.Ratio(Hex(entry.Foreground), Hex(entry.Background)) < 4.5) throw new Exception($"{palette.Id}: rendered text input contrast");
            await Capture(root, Path.Combine(output, palette.Id + ".png"));
        }
        root.Children.Remove(overlay);
        static IEnumerable<FrameworkElement> Descendants(DependencyObject element)
        {
            if (element is FrameworkElement framework) yield return framework;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(element, i))) yield return child;
        }
        var savedTheme = FeatureSettings.Read(state.FeaturesJson).Theme;
        var settingsTask = EditSettingsAsync();
        ComboBox? themePicker = null;
        for (int attempt = 0; attempt < 100 && themePicker == null; attempt++)
        {
            await Task.Delay(30);
            if (settingsWindow?.Content is DependencyObject content)
                themePicker = Descendants(content).OfType<ComboBox>().FirstOrDefault(box => box.Items.Count == AppearanceThemes.All.Count && box.Items.OfType<string>().Contains("Electric sombre"));
        }
        if (themePicker == null) throw new Exception("Theme picker unavailable.");
        foreach (var id in new[] { "electric-dark", "electric-light", "fly-dark" })
        {
            themePicker.SelectedIndex = AppearanceThemes.All.ToList().FindIndex(t => t.Id == id);
            await Task.Delay(150);
            if (FeatureSettings.Read(state.FeaturesJson).Theme != savedTheme) throw new Exception("Preview persisted before saving.");
            if (settingsWindow?.Content is not FrameworkElement settingsRoot || root.RequestedTheme != settingsRoot.RequestedTheme) throw new Exception("Settings and main window theme diverged.");
            await Capture(settingsRoot, Path.Combine(output, id + "-settings.png"));
        }
        settingsWindow!.Close(); await settingsTask;
        if (root.RequestedTheme != (AppearanceThemes.Get(savedTheme).Dark ? ElementTheme.Dark : ElementTheme.Light)) throw new Exception("Cancel did not restore theme.");
        File.WriteAllText(Path.Combine(output, "smoke-ok.txt"), $"{AppearanceThemes.All.Count} themes: rendered input, selections, list hover/selection, accent actions and popup resource contrast passed. Settings live preview and cancel passed.");
    }
}

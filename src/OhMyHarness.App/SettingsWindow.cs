using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    Window? settingsWindow;
    bool editingSettings;

    (ToggleSwitch Browser, ToggleSwitch Dom) AddBrowserSkillSettings(StackPanel panel)
    {
        var access = new ToggleSwitch { Header = T("Accès IA au navigateur"), IsOn = browserAccess.IsOn,
            OnContent = T("Autorisé"), OffContent = T("Désactivé") };
        var dom = new ToggleSwitch { Header = T("Accès DOM et interaction IA"), IsOn = browserDomAccess.IsOn,
            OnContent = T("Autorisé"), OffContent = T("Désactivé") };
        panel.Children.Add(FluentDesign.Setting(access.Header.ToString()!, "", access));
        access.Header = null;
        panel.Children.Add(FluentDesign.Setting(dom.Header.ToString()!, "", dom));
        dom.Header = null;
        return (access, dom);
    }

    async Task Settings()
    {
        if (editingSettings)
        {
            settingsWindow?.Activate();
            return;
        }
        editingSettings = true;
        try { await EditSettingsAsync(); }
        finally { editingSettings = false; }
    }

    async Task<bool> ShowSettingsWindowAsync(UIElement content, Func<bool> validate)
    {
        // Settings must never reserve approvalQueue or a ContentDialog slot:
        // agents still need to display permission requests in the main window.
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window { Title = $"{DisplayApplicationName} · {T("Réglages")}" };
        settingsWindow = window;
        var panel = new Grid
        {
            RequestedTheme = root.RequestedTheme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"),
            Padding = new Thickness(0), RowSpacing = 0
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = Label(T("Réglages"), 28);
        heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        heading.Margin = new(24, 20, 24, 12);
        panel.Children.Add(heading);
        Grid.SetRow((FrameworkElement)content, 1);
        panel.Children.Add(content);
        var save = new Button { Content = T("Enregistrer"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancel = new Button { Content = T("Annuler") };
        var actions = Row(cancel, save);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Margin = new(24, 16, 24, 16);
        Grid.SetRow(actions, 2);
        panel.Children.Add(actions);
        save.Click += (_, _) =>
        {
            if (!validate()) return;
            completed.TrySetResult(true);
            window.Close();
        };
        cancel.Click += (_, _) => window.Close();
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(settingsWindow, window)) settingsWindow = null;
            completed.TrySetResult(false);
        };
        FluentDesign.WindowChrome(window);
        ApplyBrandingIcon(window);
        window.Content = panel;
        ObserveTextZoom(panel);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1000, Height = 840 });
        window.Activate();
        return await completed.Task;
    }
}

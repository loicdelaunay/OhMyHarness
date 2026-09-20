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
        panel.Children.Add(access);
        panel.Children.Add(dom);
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
        var window = new Window { Title = $"OhMyHarness · {T("Réglages")}" };
        settingsWindow = window;
        var panel = new Grid
        {
            RequestedTheme = ElementTheme.Dark, Background = Brush(17, 20, 28),
            Padding = new Thickness(24), RowSpacing = 16
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(Label(T("Réglages"), 24));
        var scroller = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 1);
        panel.Children.Add(scroller);
        var save = new Button { Content = T("Enregistrer"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancel = new Button { Content = T("Annuler") };
        var actions = Row(cancel, save);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
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
        window.Content = panel;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 800));
        window.Activate();
        return await completed.Task;
    }
}

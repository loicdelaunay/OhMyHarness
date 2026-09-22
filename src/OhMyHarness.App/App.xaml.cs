using Microsoft.UI.Xaml;
using OhMyHarness.Core;
using System.Runtime.InteropServices;

namespace OhMyHarness.App;
public partial class App : Application
{
    Window? window;
    public App()
    {
        if(OperatingSystem.IsMacOS())Environment.SetEnvironmentVariable("PATH","/opt/homebrew/bin:/usr/local/bin:"+Environment.GetEnvironmentVariable("PATH"));
        InitializeComponent();
        FluentDesign.SetTheme("fluent-dark");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint window, string text, string caption, uint type);
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            PortableStorage.EnsureWritable();
            var smoke = Environment.GetEnvironmentVariable("OHMYHARNESS_UI_SMOKE");
            if (!string.IsNullOrWhiteSpace(smoke))
            {
                PortableStorage.UseDatabase(Path.Combine(Path.GetFullPath(smoke), "database.sqlite"));
                using var fixture = new HarnessDb(HarnessDb.DatabasePath);
                await fixture.InitializeAsync();
            }
            else await Task.Run(() =>
            {
                var legacy = Path.GetDirectoryName(HarnessDb.LegacyDatabasePath)!;
                foreach (var name in new[] { "WebView2", "OpenCodeWorkspaces" })
                    PortableStorage.ImportLegacyDirectory(Path.Combine(legacy, name), name);
            });
            window = new MainWindow();
            window.Activate();
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("OHMYHARNESS_UI_SMOKE") is { Length: > 0 } output)
            {
                Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"smoke-error.txt"),ex.ToString()); Exit(); return;
            }
            if (OperatingSystem.IsWindows()) MessageBoxW(0, "Impossible d’ouvrir les données portables. Fermez les autres instances et vérifiez les droits du dossier. / Cannot open portable data.\n\n" + ex.Message, "OhMyHarness", 0x10);
            else { window = new Window { Title = "OhMyHarness", Content = new Microsoft.UI.Xaml.Controls.TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, Margin = new(30) } }; window.Activate(); }
        }
    }
}

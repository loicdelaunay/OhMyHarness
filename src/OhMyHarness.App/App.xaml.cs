using Microsoft.UI.Xaml;
using OhMyHarness.Core;
using System.Runtime.InteropServices;

namespace OhMyHarness.App;
public partial class App : Application
{
    Window? window;
    public App() => InitializeComponent();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint window, string text, string caption, uint type);
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            PortableStorage.EnsureWritable();
            await Task.Run(() =>
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
            MessageBoxW(0, "Impossible d’ouvrir les données portables. Fermez les autres instances et vérifiez les droits du dossier. / Cannot open portable data.\n\n" + ex.Message, "OhMyHarness", 0x10);
            Exit();
        }
    }
}

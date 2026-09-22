using Uno.UI.Hosting;

namespace OhMyHarness.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--service"))
        {
            OhMyHarness.Core.Hosting.ServiceHost.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        UnoPlatformHostBuilder.Create().App(() => new App()).UseWin32().UseMacOS().UseX11().Build().Run();
    }
}

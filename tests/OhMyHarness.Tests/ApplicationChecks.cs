using OhMyHarness.Core;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

static class ApplicationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var window = new ApplicationWindow("test", 1, 1, "Test", "Title", -1920, -120, 1000, 600, true, false);
        check(window.RelativePoint(250, 120) == (-1670, 0), "Applications : coordonnées relatives et écrans négatifs");
        check((window with { X = 800, Y = 400 }).RelativePoint(250, 120) == (1050, 520), "Applications : coordonnées recalculées après déplacement");
        foreach (var point in new[] { (double.NaN, 1d), (1d, double.PositiveInfinity), (-1d, 2d), (1000d, 1d), (1d, 600d) })
        {
            bool rejected = false;
            try { window.RelativePoint(point.Item1, point.Item2); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Applications : coordonnées hors fenêtre refusées");
        }
        foreach (var invalid in new[] { window with { Minimized = true }, window with { Visible = false }, window with { Width = 20000 } })
        {
            bool rejected = false;
            try { DesktopApplications.ValidateCapture(invalid); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Applications : capture masquée/réduite/trop grande refusée");
        }
        var definitions = new JsonArray(); ApplicationTools.AddDefinitions(definitions, "");
        check(definitions.Count == 0, "Applications : skill désactivé sans outil de liste");
        ApplicationTools.AddDefinitions(definitions, "applications");
        check(definitions.Count == 1 && definitions[0]?["function"]?["name"]?.GetValue<string>() == "desktop_applications", "Applications : outil de liste exposé par le skill");
        check(AgentPolicy.Allowed("plan", "desktop_applications") && !AgentPolicy.Allowed("plan", "desktop_mouse"), "Applications : liste autorisée en Plan, souris interdite");
        if (!OperatingSystem.IsWindows()) return;
        // Own non-activated, off-screen fixture; never interact with the user's applications.
        using var dpi = DesktopApplications.PhysicalCoordinates();
        var handle = CreateWindowEx(0x08000000, "STATIC", "OhMyHarness application fixture", 0x10000000,
            -25000, -24000, 320, 240, 0, 0, 0, 0);
        if (handle == 0) throw new InvalidOperationException("Could not create application test fixture.");
        string? id = null;
        try
        {
            var found = DesktopApplications.List().Single(w => w.NativeId == handle.ToInt64()); id = found.Id;
            check(found.X == -25000 && found.Y == -24000 && found.Width == 320 && found.Height == 240, "Applications Windows : dimensions physiques de la fenêtre réelle");
            check(found.ProcessId == Environment.ProcessId && DesktopApplications.Resolve(id) == found, "Applications Windows : identité processus/fenêtre");
        }
        finally { DestroyWindow(handle); }
        bool gone = false; try { DesktopApplications.Resolve(id!); } catch (InvalidOperationException) { gone = true; }
        check(gone, "Applications Windows : fenêtre fermée refusée sans repli");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWindowEx(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint window);
}

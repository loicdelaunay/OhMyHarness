using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OhMyHarness.Cli;
using OhMyHarness.Core;

static class CliThemeChecks
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint pointer);
    public static async Task Run(Action<bool, string> check)
    {
        var ring = TerminalLogo.Outline;
        check(ring.Length == 8 && ring.All(row => row.Length == TerminalLogo.Width && row.SequenceEqual(row.Reverse())) &&
            ring.SequenceEqual(ring.Reverse()) && ring[3][3] == '.' && ring[4][4] == '.',
            "CLI icon is a hollow circle symmetric on both axes");
        var picker = new UiDialog("Theme", "", [new("crt-green", "Vert"), new("crt-amber", "Ambre")]) { PreviewTheme = true, Selected = 1 };
        check(picker.ThemePreview == "crt-amber", "Theme picker previews the initially selected theme");
        picker.Selected = 0;
        check(picker.ThemePreview == "crt-green", "Moving selection immediately changes the theme preview");
        picker.Input.Set("Ambre");
        check(picker.ThemePreview == "crt-amber", "Searching themes previews the filtered selection");
        picker.Input.Set("no match");
        check(picker.ThemePreview == null, "Empty theme search restores the session palette");
        picker.Input.Set(""); picker.Completion.TrySetResult(null);
        check(picker.ThemePreview == null, "Cancelling a theme preview restores the session theme without saving");
        var ordinary = new UiDialog("Other", "", [new("crt-green", "Vert")]);
        check(ordinary.ThemePreview == null, "Ordinary menus do not change the theme");
        check(FeatureSettings.Read(new FeatureSettings { Theme = "ivory", CliTheme = "crt-green" }.Json()) is { Theme: "ivory", CliTheme: "crt-green" }, "CLI appearance persists independently of the desktop theme");
        check(CliThemes.Resolve("shared", "ivory").Palette == Palette.FromTheme("ivory") && CliThemes.Resolve("invalid").Id == "fluent-dark", "Shared theme and unknown stored theme fall back safely");
        try { CliOptions.Parse(["--theme", "invalid"]); throw new Exception("Invalid theme accepted"); } catch (ArgumentException) { check(true, "Invalid theme option is rejected"); }
        var folder = Path.Combine(Path.GetTempPath(), "omh-theme-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fontSettings = new FeatureSettings { CliFont = "VT323", CliFontSize = 18, CliTheme = "crt-amber" };
            var customized = CliFonts.Apply(CliThemes.Resolve(fontSettings.CliTheme), fontSettings);
            check(customized.Font == "VT323" && customized.FontSize == 18 && customized.Id == "crt-amber", "Font and size override the theme without changing its palette");
            fontSettings.CliTheme = "neon-synthwave";
            check(CliFonts.Apply(CliThemes.Resolve(fontSettings.CliTheme), FeatureSettings.Read(fontSettings.Json())).Font == "VT323", "Custom CLI font survives theme changes and persistence");
            foreach (var font in CliFonts.All.Where(f => f.File != null))
            {
                var file = await CliFonts.ExportAsync(font.Face, folder);
                check(file != null && new FileInfo(file).Length > 1000 && (await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(file)!, "OFL.txt"))).Contains("SIL OPEN FONT LICENSE"), font.Face + ": embedded font exports with its original license");
            }
            var customProfile = TerminalProfiles.Build(customized, Path.Combine(folder, "omh.exe"), Path.Combine(folder, "database.sqlite"), folder);
            check(customProfile["profiles"]![0]!["font"]!["face"]!.ToString() == "VT323" && customProfile["profiles"]![0]!["font"]!["size"]!.GetValue<int>() == 18, "Exported terminal profile uses independent font and size");
            foreach (var theme in CliThemes.All)
            {
                var logoCanvas = new TerminalCanvas(TerminalLogo.Width, 4, theme.Palette.Normal);
                TerminalLogo.Draw(logoCanvas, 0, 0, theme.Palette);
                var logo = string.Join('\n', logoCanvas.Lines());
                string AnsiColor(int color) => $"38;2;{color >> 16 & 255};{color >> 8 & 255};{color & 255}";
                check(new[] { theme.Palette.Accent, theme.Palette.Green, theme.Palette.Gold }.All(color => logo.Contains(AnsiColor(color))),
                    theme.Id + ": icon colors follow the active theme");
                check(CliOptions.Parse(["--render-demo", "--theme", theme.Id]).Theme == theme.Id, theme.Id + ": preview option accepted");
                var p = theme.Palette;
                check(new[] { p.Foreground, p.Muted, p.Accent, p.Green, p.Gold }.All(fg => new[] { p.Background, p.Panel, p.Card }.All(bg => ThemeContrast.Ratio($"#{fg:X6}", $"#{bg:X6}") >= 4.5)), theme.Id + ": text contrast across all surfaces");
                var preview = Regex.Replace(TerminalUi.DemoFrame(theme.Id), "\x1b\\[[0-9;]*m", "");
                check(preview.Contains(theme.Brand) && preview.Contains("exporter mes conversations en Markdown.") &&
                    preview.TrimEnd('\r', '\n').Split('\n').All(line => TerminalText.Width(line.TrimEnd('\r')) == 118), theme.Id + ": real renderer preserves text and cell geometry");
                check(preview.Contains(theme.Border == TerminalBorder.Ascii ? "----" : "────") && !preview.Contains("╔") && !preview.Contains("╭"), theme.Id + ": minimal separator style follows the theme");
                var exe = Path.Combine(folder, "app space", "omh.exe");
                var db = Path.Combine(folder, "échanges", "database.sqlite");
                var dir = Path.Combine(folder, "projet espace") + Path.DirectorySeparatorChar;
                var json = TerminalProfiles.Build(theme, exe, db, dir);
                var profile = json["profiles"]![0]!;
                check(profile["font"]!["face"]!.GetValue<string>() == theme.Font && profile["experimental.retroTerminalEffect"]!.GetValue<bool>() && profile["updates"] == null, theme.Id + ": dedicated font/CRT profile does not update existing profiles");
                if (OperatingSystem.IsWindows())
                {
                    var argv = CommandLineToArgvW(profile["commandline"]!.GetValue<string>(), out var count);
                    if (argv == 0) throw new Exception("Command line parse failed");
                    try
                    {
                        var arguments = Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))).ToArray();
                        check(arguments.SequenceEqual(new[] { exe, "--theme", theme.Id, "--database", Path.GetFullPath(db), "--project", Path.GetFullPath(dir) }), theme.Id + ": Windows launch preserves spaces, Unicode and trailing slash");
                    }
                    finally { LocalFree(argv); }
                }
                var first = await TerminalProfiles.SaveAsync(json, folder);
                var second = await TerminalProfiles.SaveAsync(json, folder);
                check(first == second && JsonNode.Parse(await File.ReadAllTextAsync(first))!["profiles"]![0]!["guid"]!.ToString() == profile["guid"]!.ToString(), theme.Id + ": UTF-8 profile export is idempotent");
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public sealed record CliFont(string Face, string Description, string? Folder = null, string? File = null);
public static class CliFonts
{
    public static IReadOnlyList<CliFont> All { get; } = [
        new("VT323", "Terminal rétro / Retro terminal", "vt323", "VT323-Regular.ttf"),
        new("Share Tech Mono", "Science-fiction / Sci-fi", "sharetechmono", "ShareTechMono-Regular.ttf"),
        new("Space Mono", "Géométrique / Geometric", "spacemono", "SpaceMono-Regular.ttf"),
        new("Cascadia Mono", "Windows Terminal"), new("Consolas", "Classique / Classic"), new("Lucida Console", "Compact")
    ];
    public static CliTheme Apply(CliTheme theme, FeatureSettings settings) => theme with {
        Font = string.IsNullOrWhiteSpace(settings.CliFont) ? theme.Font : settings.CliFont,
        FontSize = Math.Clamp(settings.CliFontSize, 8, 36)
    };
    public static async Task<string?> ExportAsync(string face, string root, CancellationToken ct = default)
    {
        var font = All.FirstOrDefault(f => f.Face == face);
        if (font?.File == null) return null;
        var folder = Path.Combine(root, "fonts", font.Folder!); Directory.CreateDirectory(folder);
        foreach (var name in new[] { font.File, "OFL.txt" })
        {
            using var resource = typeof(CliFonts).Assembly.GetManifestResourceStream($"OhMyHarness.Cli.Fonts.{font.Folder}.{name}")
                ?? throw new IOException("Bundled font resource missing: " + name);
            await using var output = File.Create(Path.Combine(folder, name)); await resource.CopyToAsync(output, ct);
        }
        return Path.Combine(folder, font.File);
    }
    public static async Task InstallForUserAsync(string face, string root, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Install the exported font using your OS font manager.");
        var path = await ExportAsync(face, root, ct);
        if (path == null) return;
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "OhMyHarness-" + Path.GetFileName(path));
        if (!File.Exists(target)) File.Copy(path, target);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
        key.SetValue(face + " (TrueType)", target);
        _ = AddFontResourceEx(target, 0, 0);
        _ = SendMessageTimeout(new nint(0xffff), 0x001D, 0, 0, 2, 1000, out _);
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int AddFontResourceEx(string file, uint flags, nint reserved);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}

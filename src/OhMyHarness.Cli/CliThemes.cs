using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public enum TerminalBorder { Rounded, Double, Ascii }
public sealed record CliTheme(string Id, string Name, Palette Palette, string Font, int FontSize, bool Crt, TerminalBorder Border, string Brand, string Subtitle)
{
    public string Heading(string text) => Crt ? text.ToUpperInvariant() : text;
}

public static class CliThemes
{
    public static IReadOnlyList<CliTheme> All { get; } = [
        new("crt-green", "CRT · Phosphore vert", new(0x040A06, 0x0A160D, 0x102C18, 0xB6FFCB, 0x80BE93, 0x43FF87, 0x78FFAA, 0xD4FF8A), "Consolas", 12, true, TerminalBorder.Double, "O H M  / /  C R T", "PHOSPHOR GREEN · 01"),
        new("crt-amber", "CRT · Ambre", new(0x100B03, 0x1C1308, 0x352510, 0xFFE2AE, 0xC7A573, 0xFFC15A, 0xFFDA8A, 0xFFFFB5), "Lucida Console", 12, true, TerminalBorder.Ascii, "[ O H M / T E R M ]", "AMBER MONITOR · 02"),
        new("neon-synthwave", "Néon · Synthwave", new(0x100B20, 0x1B1232, 0x2C1D49, 0xF6EFFF, 0xBEB1D5, 0xFF81E5, 0x65FFE0, 0xFFE790), "Cascadia Mono", 12, true, TerminalBorder.Double, "O H M  / /  N E O N", "SYNTHWAVE · NIGHT SHIFT")
    ];
    public static bool IsValid(string id) => id == "shared" || All.Any(t => t.Id == id) || AppearanceThemes.All.Any(t => t.Id == id);
    public static CliTheme Resolve(string? id, string sharedTheme = "fluent-dark")
    {
        var dedicated = All.FirstOrDefault(t => t.Id == id);
        if (dedicated != null) return dedicated;
        var theme = AppearanceThemes.Get(id is null or "shared" ? sharedTheme : id);
        return new(theme.Id, theme.French, Palette.FromTheme(theme.Id), "Cascadia Mono", 12, false, TerminalBorder.Rounded, "◈  OH MY HARNESS", "TERMINAL WORKSPACE");
    }
}

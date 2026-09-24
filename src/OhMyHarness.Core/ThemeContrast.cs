namespace OhMyHarness.Core;

/// <summary>Shared sRGB contrast rules for desktop controls and terminal palettes.</summary>
public static class ThemeContrast
{
    static (int R, int G, int B) Rgb(string hex) => (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));
    static double Luminance(string hex)
    {
        static double Linear(int channel) { var n = channel / 255d; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        var (r, g, b) = Rgb(hex); return .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);
    }
    public static double Ratio(string a, string b)
    { var x = Luminance(a); var y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
    public static string Blend(string a, string b, double amount)
    {
        var x = Rgb(a); var y = Rgb(b);
        return $"#{(int)Math.Round(x.R + (y.R - x.R) * amount):X2}{(int)Math.Round(x.G + (y.G - x.G) * amount):X2}{(int)Math.Round(x.B + (y.B - x.B) * amount):X2}";
    }
    public static string On(string background) => Ratio("#0E0F12", background) >= Ratio("#FFFFFF", background) ? "#0E0F12" : "#FFFFFF";
    public static string Readable(string foreground, string background, double minimum = 4.5)
    {
        var target = On(background);
        for (int step = 0; step <= 100; step++)
        {
            var candidate = Blend(foreground, target, step / 100d);
            if (Ratio(candidate, background) >= minimum) return candidate;
        }
        return target;
    }
    public static string AccentText(AppearanceTheme theme) => Readable(Readable(theme.Accent, theme.Surface), theme.Background);
    // WinUI's native text editor paints selected glyphs white on some platforms.
    public static string Selection(AppearanceTheme theme) => Readable(theme.Accent, "#FFFFFF");
}

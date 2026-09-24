using System.Runtime.InteropServices;
using System.Text;

namespace OhMyHarness.Cli;

public sealed record Ink(int Foreground, int Background, bool Bold = false);
public sealed record Palette(int Background, int Panel, int Card, int Foreground, int Muted, int Accent, int Green, int Gold)
{
    public static Palette FromTheme(string id)
    {
        var theme = OhMyHarness.Core.AppearanceThemes.Get(id);
        static int Hex(string hex) => Convert.ToInt32(hex[1..], 16);
        return new(Hex(theme.Background), Hex(theme.Surface), Hex(OhMyHarness.Core.ThemeContrast.Blend(theme.Surface, theme.Accent, .12)),
            Hex(theme.Text), Hex(theme.Muted), Hex(OhMyHarness.Core.ThemeContrast.AccentText(theme)),
            theme.Dark ? 0x77d5b0 : 0x176443, theme.Dark ? 0xf2c57c : 0x86561a);
    }
    public static Palette Midnight { get; } = new(0x0b101a, 0x101827, 0x172338, 0xe4eaf5, 0x8291ac, 0x7ab4ff, 0x77d5b0, 0xf2c57c);
    public static Palette Light { get; } = new(0xf3f5fa, 0xe8edf5, 0xdce5f3, 0x18273d, 0x536882, 0x1854ab, 0x176443, 0x86561a);
    public Ink Normal => new(Foreground, Background);
    public Ink Dim => new(Muted, Background);
    public Ink Highlight => new(Accent, Background, true);
}

public sealed class TerminalCanvas
{
    readonly string[,] cells;
    readonly Ink[,] inks;
    readonly TerminalBorder border;
    public int Width { get; }
    public int Height { get; }
    public TerminalCanvas(int width, int height, Ink ink, TerminalBorder border = TerminalBorder.Rounded)
    {
        Width = width; Height = height; this.border = border; cells = new string[height, width]; inks = new Ink[height, width];
        Fill(0, 0, width, height, ink);
    }
    public void Fill(int x, int y, int width, int height, Ink ink)
    {
        for (int row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
            for (int col = Math.Max(0, x); col < Math.Min(Width, x + width); col++) { cells[row, col] = " "; inks[row, col] = ink; }
    }
    public void Write(int x, int y, string value, Ink ink, int maxWidth = int.MaxValue)
    {
        if (y < 0 || y >= Height) return; int end = Math.Min(Width, x + Math.Min(maxWidth, Width));
        foreach (var e in TerminalText.Elements(value.Replace('\n', ' ')))
        {
            if (x < 0 || x + e.Width > end) break;
            cells[y, x] = e.Text; inks[y, x] = ink;
            if (e.Width == 2) { cells[y, x + 1] = ""; inks[y, x + 1] = ink; }
            x += e.Width;
        }
    }
    public void Box(int x, int y, int width, int height, Ink ink)
    {
        if (width < 2 || height < 2) return;
        var glyphs = border switch { TerminalBorder.Double => "╔╗╚╝═║", TerminalBorder.Ascii => "++++-|", _ => "╭╮╰╯─│" };
        Write(x, y, glyphs[0] + new string(glyphs[4], width - 2) + glyphs[1], ink);
        Write(x, y + height - 1, glyphs[2] + new string(glyphs[4], width - 2) + glyphs[3], ink);
        for (int row = y + 1; row < y + height - 1; row++) { Write(x, row, glyphs[5].ToString(), ink); Write(x + width - 1, row, glyphs[5].ToString(), ink); }
    }
    public string[] Lines()
    {
        var rows = new string[Height];
        for (int y = 0; y < Height; y++)
        {
            var line = new StringBuilder(); Ink? previous = null;
            for (int x = 0; x < Width; x++)
            {
                var ink = inks[y, x];
                if (ink != previous) { line.Append(Ansi(ink)); previous = ink; }
                line.Append(cells[y, x]);
            }
            rows[y] = line.Append("\x1b[0m").ToString();
        }
        return rows;
    }
    static string Rgb(int color) => $"{color >> 16 & 255};{color >> 8 & 255};{color & 255}";
    static string Ansi(Ink ink) => $"\x1b[0;{(ink.Bold ? "1;" : "")}38;2;{Rgb(ink.Foreground)};48;2;{Rgb(ink.Background)}m";
}

public sealed class TerminalSession : IDisposable
{
    readonly bool oldControlC;
    readonly uint oldMode;
    readonly nint output;
    string[] previous = [];
    [DllImport("kernel32.dll")] static extern nint GetStdHandle(int handle);
    [DllImport("kernel32.dll")] static extern bool GetConsoleMode(nint handle, out uint mode);
    [DllImport("kernel32.dll")] static extern bool SetConsoleMode(nint handle, uint mode);
    public TerminalSession()
    {
        oldControlC = Console.TreatControlCAsInput; Console.TreatControlCAsInput = true;
        if (OperatingSystem.IsWindows()) { output = GetStdHandle(-11); if (GetConsoleMode(output, out oldMode)) SetConsoleMode(output, oldMode | 4); }
        Console.Write("\x1b[?1049h\x1b[?25l\x1b[?2004h\x1b[?1000h\x1b[?1006h\x1b[2J");
    }
    public void Paint(TerminalCanvas canvas, bool force = false)
    {
        var lines = canvas.Lines(); var output = new StringBuilder("\x1b[?25l");
        for (int y = 0; y < lines.Length; y++)
            if (force || y >= previous.Length || previous[y] != lines[y]) output.Append($"\x1b[{y + 1};1H").Append(lines[y]);
        Console.Write(output.ToString()); previous = lines;
    }
    public void Dispose()
    {
        Console.Write("\x1b[0m\x1b[?1000l\x1b[?1006l\x1b[?2004l\x1b[?25h\x1b[?1049l");
        Console.TreatControlCAsInput = oldControlC;
        if (OperatingSystem.IsWindows() && oldMode != 0) SetConsoleMode(output, oldMode);
    }
}

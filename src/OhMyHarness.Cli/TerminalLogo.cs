namespace OhMyHarness.Cli;

/// <summary>A symmetric ring drawn with the active terminal theme.</summary>
internal static class TerminalLogo
{
    internal static readonly string[] Outline =
    [
        "..####..",
        ".##..##.",
        "##....##",
        "#......#",
        "#......#",
        "##....##",
        ".##..##.",
        "..####.."
    ];

    public const int Width = 8;

    public static void Draw(TerminalCanvas canvas, int x, int y, Palette palette)
    {
        for (int row = 0; row < Outline.Length; row += 2)
            for (int column = 0; column < Width; column++)
            {
                int top = Color(row, column, palette), bottom = Color(row + 1, column, palette);
                if (top == 0 && bottom == 0) continue;
                var symbol = top == 0 ? "▄" : "▀";
                canvas.Write(x + column, y + row / 2, symbol,
                    new(top == 0 ? bottom : top, top == 0 || bottom == 0 ? palette.Background : bottom), 1);
            }
    }

    private static int Color(int row, int column, Palette palette)
    {
        if (Outline[row][column] == '.') return 0;
        return Math.Min(row, Outline.Length - 1 - row) switch
        {
            0 => palette.Accent,
            1 => palette.Green,
            2 => palette.Gold,
            _ => palette.Accent
        };
    }
}

namespace OhMyHarness.Cli;

/// <summary>A compact, terminal-safe rendering of the application's ring logo.</summary>
internal static class TerminalLogo
{
    // Sampled from Assets/logo-64.png. Zeroes are transparent so every CLI theme supplies its own background.
    private static readonly int[,] Pixels =
    {
        { 0, 0, 0, 0, 0, 0, 0, 0 },
        { 0, 0, 0, 0x6540A6, 0x4B57AD, 0, 0, 0 },
        { 0, 0, 0x6634BD, 0x5C93D3, 0x4FE5F2, 0x13E0F8, 0, 0 },
        { 0, 0x6337A2, 0x7938D6, 0, 0, 0x1195FE, 0x51499A, 0 },
        { 0, 0x6643B3, 0x5571F3, 0, 0, 0xA83EDB, 0, 0 },
        { 0, 0, 0x1CC8F3, 0x46E2F7, 0xD550BD, 0xFE1BC5, 0, 0 },
        { 0, 0, 0, 0, 0, 0, 0, 0 },
        { 0, 0, 0, 0, 0, 0, 0, 0 }
    };

    public const int Width = 8;

    public static void Draw(TerminalCanvas canvas, int x, int y, int background)
    {
        for (int row = 0; row < Pixels.GetLength(0); row += 2)
            for (int column = 0; column < Width; column++)
            {
                int top = Pixels[row, column], bottom = Pixels[row + 1, column];
                if (top == 0 && bottom == 0) continue;
                var symbol = top == 0 ? "▄" : "▀";
                canvas.Write(x + column, y + row / 2, symbol,
                    new(top == 0 ? bottom : top, top == 0 || bottom == 0 ? background : bottom), 1);
            }
    }
}

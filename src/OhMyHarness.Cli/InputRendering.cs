namespace OhMyHarness.Cli;

static class InputRendering
{
    // Source offsets stay UTF-16 offsets, while layout uses terminal cells (including wide glyphs).
    public static void Draw(TerminalCanvas canvas, InputBuffer input, int x, int y, int width, int height,
        Palette palette, bool crt = false, bool secret = false)
    {
        width = Math.Max(2, width);
        var cells = new List<(int X, int Y, string Text, bool Selected, bool Cursor)>();
        int col = 0, row = 0, offset = 0, cursorRow = 0;
        void Add(string text, int size, bool selected, bool cursor = false)
        {
            if (col + size > width) { col = 0; row++; }
            if (cursor) cursorRow = row;
            cells.Add((col, row, text, selected, cursor)); col += size;
        }
        foreach (var element in TerminalText.Elements(input.Text))
        {
            if (offset == input.Cursor) Add(crt ? "█" : "▏", 1, false, true);
            bool selected = offset >= input.SelectionStart && offset < input.SelectionStart + input.SelectionLength;
            if (element.Text == "\n")
            {
                if (selected) Add(" ", 1, true);
                col = 0; row++;
            }
            else Add(secret ? "•" : element.Text, secret ? 1 : element.Width, selected);
            offset += element.Text.Length;
        }
        if (offset == input.Cursor) Add(crt ? "█" : "▏", 1, false, true);
        int first = Math.Max(0, cursorRow - height + 1);
        foreach (var cell in cells)
            if (cell.Y >= first && cell.Y < first + height)
                canvas.Write(x + cell.X, y + cell.Y - first, cell.Text,
                    cell.Selected ? new Ink(palette.Background, palette.Accent) : cell.Cursor ? palette.Highlight : palette.Normal);
    }
}

using System.Globalization;
using System.Text;

namespace OhMyHarness.Cli;

public sealed class TerminalPaste
{
    readonly StringBuilder content = new();
    string tail = "";
    public bool TooLong { get; private set; }
    public bool Feed(char character)
    {
        if (content.Length < 128_006) content.Append(character); else TooLong = true;
        tail += character;
        if (tail.Length > 6) tail = tail[1..];
        return tail == "\x1b[201~";
    }
    public string Take()
    {
        string value = TooLong ? "" : content.ToString(0, Math.Max(0, content.Length - 6)).Replace("\r\n", "\n").Replace('\r', '\n');
        content.Clear(); tail = ""; TooLong = false;
        return value;
    }
}

public static class TerminalText
{
    // Model/tool output must never be able to write terminal control sequences (OSC, CSI, etc.).
    public static string Clean(string value)
    {
        var text = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value == '\n') text.Append('\n');
            else if (rune.Value == '\t') text.Append("    ");
            else if (!Rune.IsControl(rune) && Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) text.Append(rune);
        }
        return text.ToString();
    }
    public static IEnumerable<(string Text, int Width)> Elements(string value)
    {
        var elements = StringInfo.GetTextElementEnumerator(Clean(value));
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement(); var n = Rune.GetRuneAt(element, 0).Value;
            var wide = n is >= 0x1100 and <= 0x115f or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3 or >= 0xf900 and <= 0xfaff
                or >= 0xfe10 and <= 0xfe6f or >= 0xff00 and <= 0xff60 or >= 0x1f300 and <= 0x1faff or >= 0x20000;
            yield return (element, wide ? 2 : 1);
        }
    }
    public static int Width(string value) => Elements(value).Sum(e => e.Width);
    public static string Fit(string value, int width)
    {
        var output = new StringBuilder(); int used = 0;
        foreach (var e in Elements(value.Replace('\n', ' '))) { if (used + e.Width > width) break; output.Append(e.Text); used += e.Width; }
        return output.ToString();
    }
    public static List<string> Wrap(string value, int width)
    {
        width = Math.Max(1, width); var rows = new List<string>();
        foreach (var line in Clean(value).Split('\n'))
        {
            var row = new StringBuilder(); int used = 0;
            foreach (var e in Elements(line))
            {
                if (used + e.Width > width) { rows.Add(row.ToString()); row.Clear(); used = 0; }
                row.Append(e.Text); used += e.Width;
            }
            rows.Add(row.ToString());
        }
        return rows;
    }
}

public sealed class InputBuffer
{
    public string Text { get; private set; } = "";
    public int Cursor { get; private set; }
    public void Set(string value) { Text = TerminalText.Clean(value); Cursor = Text.Length; }
    public void Insert(string value)
    {
        value = TerminalText.Clean(value);
        if (Text.Length + value.Length > 128_000) return;
        Text = Text.Insert(Cursor, value); Cursor += value.Length;
    }
    public void Key(ConsoleKeyInfo key)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(Text);
        int Previous() => boundaries.LastOrDefault(b => b < Cursor, 0);
        int Next() => boundaries.FirstOrDefault(b => b > Cursor, Text.Length);
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow: Cursor = Previous(); break;
            case ConsoleKey.RightArrow: Cursor = Next(); break;
            case ConsoleKey.Home: Cursor = 0; break;
            case ConsoleKey.End: Cursor = Text.Length; break;
            case ConsoleKey.Backspace when Cursor > 0: int start = Previous(); Text = Text.Remove(start, Cursor - start); Cursor = start; break;
            case ConsoleKey.Delete when Cursor < Text.Length: Text = Text.Remove(Cursor, Next() - Cursor); break;
            case ConsoleKey.U when key.Modifiers.HasFlag(ConsoleModifiers.Control): Set(""); break;
            default: if (!char.IsControl(key.KeyChar)) Insert(key.KeyChar.ToString()); break;
        }
    }
}

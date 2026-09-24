using System.Globalization;

namespace OhMyHarness.Cli;

public sealed class InputBuffer
{
    public const int MaxLength = 128_000;
    public string Text { get; private set; } = "";
    public int Cursor { get; private set; }
    int anchor;
    int? desiredColumn;
    readonly List<(string Text, int Cursor, int Anchor)> undo = [], redo = [];
    public int SelectionStart => Math.Min(anchor, Cursor);
    public int SelectionLength => Math.Abs(anchor - Cursor);
    public bool HasSelection => anchor != Cursor;
    public string SelectedText => Text.Substring(SelectionStart, SelectionLength);
    public void Set(string value)
    {
        Text = TerminalText.Clean(value); Cursor = anchor = Text.Length;
        desiredColumn = null; undo.Clear(); redo.Clear();
    }
    void Save()
    {
        undo.Add((Text, Cursor, anchor));
        if (undo.Count > 32) undo.RemoveAt(0);
        redo.Clear(); desiredColumn = null;
    }
    void Restore(List<(string Text, int Cursor, int Anchor)> from, List<(string Text, int Cursor, int Anchor)> to)
    {
        if (from.Count == 0) return;
        to.Add((Text, Cursor, anchor));
        (Text, Cursor, anchor) = from[^1]; from.RemoveAt(from.Count - 1); desiredColumn = null;
    }
    public void Insert(string value)
    {
        value = TerminalText.Clean(value.Replace("\r\n", "\n").Replace('\r', '\n'));
        if (value.Length == 0 || Text.Length - SelectionLength + value.Length > MaxLength) return;
        Save(); int start = SelectionStart;
        Text = Text.Remove(start, SelectionLength).Insert(start, value);
        Cursor = anchor = start + value.Length;
    }
    public void DeleteSelection()
    {
        if (HasSelection) Delete(SelectionStart, SelectionStart + SelectionLength);
    }
    void Delete(int start, int end)
    {
        if (start == end) return;
        Save(); Text = Text.Remove(start, end - start); Cursor = anchor = start;
    }
    void Move(int target, bool select, bool vertical = false)
    {
        Cursor = target;
        if (!select) anchor = Cursor;
        if (!vertical) desiredColumn = null;
    }
    string? indexedText;
    int[] boundaries = [];
    int Previous(int position)
    {
        if (!ReferenceEquals(indexedText, Text)) { boundaries = StringInfo.ParseCombiningCharacters(Text); indexedText = Text; }
        int index = Array.BinarySearch(boundaries, position);
        index = index >= 0 ? index - 1 : ~index - 1;
        return index < 0 ? 0 : boundaries[index];
    }
    int Next(int position) => position >= Text.Length ? Text.Length : position + StringInfo.GetNextTextElementLength(Text, position);
    // Group letters/numbers, punctuation and whitespace separately; never split a grapheme.
    int Kind(int position) => char.IsWhiteSpace(Text, position) ? 0 : char.IsLetterOrDigit(Text, position) || Text[position] == '_' ? 1 : 2;
    int WordLeft()
    {
        int pos = Cursor;
        while (pos > 0 && Kind(Previous(pos)) == 0) pos = Previous(pos);
        if (pos == 0) return 0;
        int kind = Kind(Previous(pos));
        while (pos > 0 && Kind(Previous(pos)) == kind) pos = Previous(pos);
        return pos;
    }
    int WordRight()
    {
        int pos = Cursor;
        if (pos < Text.Length)
        {
            int kind = Kind(pos);
            while (pos < Text.Length && Kind(pos) == kind) pos = Next(pos);
        }
        while (pos < Text.Length && Kind(pos) == 0) pos = Next(pos);
        return pos;
    }
    int LineStart(int pos) => pos == 0 ? 0 : Text.LastIndexOf('\n', pos - 1) + 1;
    int LineEnd(int pos) { int end = Text.IndexOf('\n', pos); return end < 0 ? Text.Length : end; }
    int Vertical(bool down)
    {
        int start = LineStart(Cursor);
        desiredColumn ??= TerminalText.Width(Text[start..Cursor]);
        int nextStart = down ? Math.Min(Text.Length, LineEnd(Cursor) + 1) : start == 0 ? 0 : LineStart(start - 1);
        if (down && LineEnd(Cursor) == Text.Length) return Text.Length;
        if (!down && start == 0) return 0;
        int pos = nextStart, end = LineEnd(nextStart), column = 0;
        while (pos < end)
        {
            int next = Next(pos), width = TerminalText.Width(Text[pos..next]);
            if (column + width > desiredColumn) break;
            column += width; pos = next;
        }
        return pos;
    }
    public void Key(ConsoleKeyInfo key)
    {
        bool ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control), shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
        switch (key.Key)
        {
            case ConsoleKey.A when ctrl: anchor = 0; Cursor = Text.Length; desiredColumn = null; break;
            case ConsoleKey.Z when ctrl && shift: Restore(redo, undo); break;
            case ConsoleKey.Z when ctrl: Restore(undo, redo); break;
            case ConsoleKey.Y when ctrl: Restore(redo, undo); break;
            case ConsoleKey.LeftArrow: Move(!shift && HasSelection ? SelectionStart : ctrl ? WordLeft() : Previous(Cursor), shift); break;
            case ConsoleKey.RightArrow: Move(!shift && HasSelection ? SelectionStart + SelectionLength : ctrl ? WordRight() : Next(Cursor), shift); break;
            case ConsoleKey.UpArrow: Move(ctrl ? 0 : Vertical(false), shift, !ctrl); break;
            case ConsoleKey.DownArrow: Move(ctrl ? Text.Length : Vertical(true), shift, !ctrl); break;
            case ConsoleKey.Home: Move(ctrl ? 0 : LineStart(Cursor), shift); break;
            case ConsoleKey.End: Move(ctrl ? Text.Length : LineEnd(Cursor), shift); break;
            case ConsoleKey.Backspace:
                if (HasSelection) DeleteSelection(); else Delete(ctrl ? WordLeft() : Previous(Cursor), Cursor); break;
            case ConsoleKey.Delete:
                if (HasSelection) DeleteSelection(); else Delete(Cursor, ctrl ? WordRight() : Next(Cursor)); break;
            case ConsoleKey.W when ctrl:
                if (HasSelection) DeleteSelection(); else Delete(WordLeft(), Cursor); break;
            case ConsoleKey.U when ctrl: Delete(0, Text.Length); break;
            default: if (!char.IsControl(key.KeyChar) && !ctrl && !key.Modifiers.HasFlag(ConsoleModifiers.Alt)) Insert(key.KeyChar.ToString()); break;
        }
    }
}

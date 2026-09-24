using System.Text.RegularExpressions;
using OhMyHarness.Cli;

static class InputChecks
{
    public static void Run(Action<bool, string> check)
    {
        static ConsoleKeyInfo Key(ConsoleKey key, bool ctrl = false, bool shift = false) => new('\0', key, shift, false, ctrl);
        var input = new InputBuffer();
        input.Set("Bonjour le monde\nligne 2"); input.Key(TerminalKeys.Character('\x01'));
        check(input.SelectedText == input.Text, "Ctrl+A selects the entire multiline draft");
        input.Insert("remplacé");
        check(input.Text == "remplacé" && !input.HasSelection, "Typing replaces the selection");
        input.Key(Key(ConsoleKey.Z, true));
        check(input.Text == "Bonjour le monde\nligne 2" && input.HasSelection, "Undo restores text and selection");
        input.Key(Key(ConsoleKey.Y, true));
        check(input.Text == "remplacé", "Ctrl+Y redoes the edit");
        input.Key(Key(ConsoleKey.Z, true)); input.Key(Key(ConsoleKey.Z, true, true));
        check(input.Text == "remplacé", "Ctrl+Shift+Z also redoes the edit");
        input.Set("bonjour le monde");
        input.Key(TerminalKeys.Sequence("\x1b[1;6D")!.Value);
        check(input.SelectedText == "monde", "VT Ctrl+Shift+Left selects the previous word");
        input.Key(TerminalKeys.Sequence("\x1b[1;6D")!.Value);
        check(input.SelectedText == "le monde", "Repeated word selection extends from the same anchor");
        input.Key(TerminalKeys.Sequence("\x1b[1;6C")!.Value);
        check(input.SelectedText == "monde", "Opposite word movement shrinks the selection");
        input.Key(Key(ConsoleKey.LeftArrow));
        check(input.Cursor == 11 && !input.HasSelection, "Unmodified arrow collapses selection to its edge");
        input.Key(TerminalKeys.Sequence("\x1b[1;5D")!.Value);
        check(input.Cursor == 8, "Ctrl+Left navigates by word");
        input.Key(TerminalKeys.Sequence("\x1b[3;5~")!.Value);
        check(input.Text == "bonjour monde", "Ctrl+Delete deletes the following word");
        input.Key(Key(ConsoleKey.Backspace, true));
        check(input.Text == "monde" && input.Cursor == 0, "Ctrl+Backspace deletes the previous word when delivered by the host");
        input.Set("a😀e\u0301"); input.Key(Key(ConsoleKey.LeftArrow, shift: true));
        check(input.SelectedText == "e\u0301", "Shift selection preserves combining characters");
        input.Key(Key(ConsoleKey.LeftArrow, shift: true)); input.Key(Key(ConsoleKey.Delete));
        check(input.Text == "a", "Deleting selection preserves emoji/grapheme boundaries");
        input.Set("abc\nx\nabcdef"); input.Key(Key(ConsoleKey.LeftArrow)); input.Key(Key(ConsoleKey.LeftArrow));
        input.Key(Key(ConsoleKey.UpArrow)); input.Key(Key(ConsoleKey.UpArrow)); input.Key(Key(ConsoleKey.DownArrow)); input.Key(Key(ConsoleKey.DownArrow));
        check(input.Cursor == 10, "Vertical navigation retains preferred column through shorter lines");
        input.Key(Key(ConsoleKey.Home)); check(input.Cursor == 6, "Home moves to the current line start");
        input.Key(Key(ConsoleKey.Home, true, true));
        check(input.SelectedText == "abc\nx\n", "Ctrl+Shift+Home selects to document start");
        input.Key(Key(ConsoleKey.End, true, true));
        check(input.SelectedText == "abcdef", "Selection can cross its anchor without corrupting text");
        input.Set(new string('x', InputBuffer.MaxLength)); input.Key(Key(ConsoleKey.A, true)); input.Insert("ok");
        check(input.Text == "ok", "Replacement checks size after removing selection");
        input.Key(Key(ConsoleKey.A, true)); input.Insert(new string('x', InputBuffer.MaxLength + 1));
        check(input.Text == "ok" && input.HasSelection, "Oversized replacement preserves the existing selection");
        input.Insert("a\r\nb"); check(input.Text == "a\nb", "Clipboard line endings remain multiline text");
        input.Set("/co"); input.Key(Key(ConsoleKey.A, true));
        var completion = new CommandCompletion([new("/connect", "Connect")]);
        check(completion.Matches(input).Count == 0 && !completion.Handle(Key(ConsoleKey.UpArrow, shift: true), input), "Selection shortcuts do not trigger slash completion");
        foreach (var (sequence, key, modifiers) in new[]
        {
            ("\x1b[1;2D", ConsoleKey.LeftArrow, ConsoleModifiers.Shift),
            ("\x1b[1;6C", ConsoleKey.RightArrow, ConsoleModifiers.Control | ConsoleModifiers.Shift),
            ("\x1b[1;5H", ConsoleKey.Home, ConsoleModifiers.Control),
            ("\x1b[8;6~", ConsoleKey.End, ConsoleModifiers.Control | ConsoleModifiers.Shift),
            ("\x1b[1;6A", ConsoleKey.UpArrow, ConsoleModifiers.Control | ConsoleModifiers.Shift)
        })
            check(TerminalKeys.Sequence(sequence) is { } decoded && decoded.Key == key && decoded.Modifiers == modifiers, "VT decoder retains modifiers: " + sequence[2..]);
        check(new[] { "\x1b[1;", "\x1b[1;99D", "\x1b[<0;1;1M", "\x1b[200~", "\x1b[201~" }.All(s => TerminalKeys.Sequence(s) == null), "Partial, mouse and paste sequences are not keyboard shortcuts");
        foreach (char backspace in new[] { '\b', '\x7f' })
        {
            input.Set("un mot😀"); input.Key(TerminalKeys.Character(backspace));
            check(input.Text == "un mot", "Both terminal Backspace encodings delete a single grapheme");
        }
        input.Set("un mot"); input.Key(TerminalKeys.Sequence("\x1b[127;5u")!.Value);
        check(input.Text == "un ", "Explicit Ctrl+Backspace still deletes a word");
        check(TerminalKeys.Sequence("\x1b[122;6u") is { Key: ConsoleKey.Z, Modifiers: ConsoleModifiers.Control | ConsoleModifiers.Shift }, "Extended keyboard protocol preserves Ctrl+Shift+Z");
        bool failed = false; string? copied = null;
        input.Set("draft"); input.Key(Key(ConsoleKey.A, true));
        bool Handle(ConsoleKey key, bool secret = false, bool success = true, string? paste = "new\ntext") =>
            InputShortcuts.HandleClipboard(input, Key(key, true), secret, text => { copied = text; return success; }, () => paste, () => failed = true);
        check(Handle(ConsoleKey.C) && copied == "draft" && input.Text == "draft", "Copy selection consumes Ctrl+C without stopping the agent");
        copied = null; Handle(ConsoleKey.X, success: false);
        check(failed && input.Text == "draft", "Clipboard failure never deletes selected text");
        Handle(ConsoleKey.X); check(input.Text == "" && copied == "draft", "Cut removes text only after copying successfully");
        Handle(ConsoleKey.V); check(input.Text == "new\ntext", "Ctrl+V inserts multiline text without sending it");
        check(!Handle(ConsoleKey.C), "Ctrl+C without selection remains available to stop the agent");
        input.Key(Key(ConsoleKey.A, true)); copied = null; Handle(ConsoleKey.C, secret: true);
        check(copied == null && input.Text == "new\ntext", "Masked credentials cannot be copied or cut");
        input.Set("abcdefghi😀\nsecond"); input.Key(Key(ConsoleKey.A, true));
        var palette = Palette.Midnight; var canvas = new TerminalCanvas(12, 3, palette.Normal);
        InputRendering.Draw(canvas, input, 0, 0, 10, 3, palette);
        var lines = canvas.Lines();
        check(lines.Any(l => l.Contains("48;2;122;180;255")) && lines.All(l => TerminalText.Width(Regex.Replace(l, "\x1b\\[[0-9;]*m", "")) == 12), "Selection is highlighted and wrapped wide glyphs stay inside the viewport");
        var secretCanvas = new TerminalCanvas(12, 1, palette.Normal);
        InputRendering.Draw(secretCanvas, input, 0, 0, 10, 1, palette, secret: true);
        check(!string.Join("", secretCanvas.Lines()).Contains("second") && string.Join("", secretCanvas.Lines()).Contains('•'), "Secret editor renders masked text with a visible caret");
    }
}

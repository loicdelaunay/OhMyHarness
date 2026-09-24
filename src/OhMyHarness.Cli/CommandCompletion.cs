namespace OhMyHarness.Cli;

public sealed class CommandCompletion(IReadOnlyList<Choice> commands)
{
    string previous = "";
    bool dismissed;
    public int Selected { get; private set; }

    public List<Choice> Matches(InputBuffer input)
    {
        if (previous != input.Text) { previous = input.Text; Selected = 0; dismissed = false; }
        if (dismissed || !input.Text.StartsWith('/') || input.Text.Any(char.IsWhiteSpace) || input.Cursor != input.Text.Length) return [];
        return commands.Where(c => c.Value.StartsWith(input.Text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public bool Handle(ConsoleKeyInfo key, InputBuffer input)
    {
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control) || key.Modifiers.HasFlag(ConsoleModifiers.Alt)) return false;
        var matches = Matches(input);
        if (matches.Count == 0) return false;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow: Selected = (Selected + matches.Count - 1) % matches.Count; return true;
            case ConsoleKey.DownArrow: Selected = (Selected + 1) % matches.Count; return true;
            case ConsoleKey.Escape: dismissed = true; return true;
            case ConsoleKey.Tab:
            case ConsoleKey.Enter:
                input.Set(matches[Selected].Value + " ");
                return true;
            default: return false;
        }
    }
}

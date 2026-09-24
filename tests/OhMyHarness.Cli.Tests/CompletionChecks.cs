using System.Text.RegularExpressions;
using OhMyHarness.Cli;

static class CompletionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var completion = new CommandCompletion([new("/connect", "Connexion"), new("/context", "Contexte"), new("/help", "Aide")]);
        var input = new InputBuffer();
        ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);
        input.Set("/");
        check(completion.Matches(input).Count == 3, "Slash immediately offers commands");
        input.Set("/CO");
        check(completion.Matches(input).Count == 2, "Completion filters command prefixes case-insensitively");
        completion.Handle(Key(ConsoleKey.DownArrow), input);
        check(completion.Handle(TerminalKeys.Character('\t'), input) && input.Text == "/context ", "Arrow selection and Tab complete the chosen command with room for arguments");
        check(completion.Matches(input).Count == 0 && !completion.Handle(Key(ConsoleKey.Enter), input), "Completed command leaves Enter available for execution");
        input.Set("/co");
        completion.Handle(Key(ConsoleKey.UpArrow), input);
        check(completion.Selected == 1, "Completion navigation wraps upward");
        check(completion.Handle(Key(ConsoleKey.Escape), input) && input.Text == "/co" && completion.Matches(input).Count == 0, "Escape consumes dismissal without stopping the agent or changing the draft");
        input.Insert("n");
        check(completion.Matches(input).Count == 2 && completion.Selected == 0, "Editing after dismissal reopens filtered suggestions");
        completion.Handle(Key(ConsoleKey.Enter), input);
        check(input.Text == "/connect ", "Enter accepts a suggestion without executing it");
        foreach (var text in new[] { "hello /co", "/connect key", "/co\ntext", "/unknown" })
        {
            input.Set(text);
            check(completion.Matches(input).Count == 0, "No command completion inside messages, arguments, multiline text or unknown commands: " + text);
        }
        input.Set("/co"); input.Key(Key(ConsoleKey.Home));
        check(completion.Matches(input).Count == 0, "Editing before the command end preserves ordinary cursor navigation");
        input.Key(Key(ConsoleKey.End));
        check(!completion.Handle(new('\r', ConsoleKey.Enter, false, true, false), input), "Multiline shortcut bypasses completion");
        foreach (var (width, height) in new[] { (56, 18), (80, 24), (118, 36) })
        {
            var frame = Regex.Replace(TerminalUi.DemoFrame(width: width, height: height, input: "/co"), "\x1b\\[[0-9;]*m", "");
            var lines = frame.TrimEnd('\r', '\n').Split('\n');
            check(lines.Length == height && lines.All(l => TerminalText.Width(l.TrimEnd('\r')) == width)
                && frame.Contains("/connect") && frame.Contains("/co▏"), $"Suggestions and composer remain visible at {width}x{height}");
        }
    }
}

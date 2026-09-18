namespace OhMyHarness.Core;

public sealed record KeyboardChord(IReadOnlyList<string> Modifiers, string Key);

public static class KeyboardInput
{
    static readonly HashSet<string> Modifiers = ["CTRL", "ALT", "SHIFT", "WIN"];
    static readonly HashSet<string> NamedKeys = [
        "ENTER", "TAB", "ESCAPE", "SPACE", "BACKSPACE", "DELETE", "INSERT", "HOME", "END",
        "PAGEUP", "PAGEDOWN", "LEFT", "RIGHT", "UP", "DOWN", "PRINTSCREEN", "CAPSLOCK", "PLUS", "MINUS"
    ];

    public static IReadOnlyList<string> SupportedKeys { get; } =
        Modifiers.Concat(NamedKeys).Concat(Enumerable.Range('A', 26).Select(x => ((char)x).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(x => x.ToString()))
            .Concat(Enumerable.Range(1, 24).Select(x => "F" + x)).ToArray();

    public static IEnumerable<string> KeysForPlatform(bool macOS) => macOS
        ? SupportedKeys.Where(x => x is not ("INSERT" or "PRINTSCREEN" or "F21" or "F22" or "F23" or "F24")) : SupportedKeys;

    public static string DescribeKeys() => System.Text.Json.JsonSerializer.Serialize(new
    {
        platform = OperatingSystem.IsMacOS() ? "macOS" : "Windows", shell = PlatformSupport.ShellName,
        commandKey = OperatingSystem.IsMacOS() ? "CMD (normalized to WIN)" : "WIN",
        actions = new[] { "press", "type" }, keys = KeysForPlatform(OperatingSystem.IsMacOS()),
        aliases = new[] { "CONTROL=CTRL", "WINDOWS/META/CMD=WIN", "ESC=ESCAPE", "RETURN/ENTRÉE=ENTER", "DEL=DELETE", "INS=INSERT", "PGUP=PAGEUP", "PGDN=PAGEDOWN" },
        examples = new[] { "ALT", "CTRL", "SHIFT", "WIN", "ENTER", "CTRL+S", "ALT+TAB", "CTRL+SHIFT+S", "CTRL+PLUS" },
        usage = "press sends a complete key down/up, including a standalone modifier. Use type for arbitrary text. Keys are case-insensitive; separate modifiers with +. Keys are never left held down."
    });

    public static KeyboardChord ParseChord(string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord)) throw new ArgumentException("A keyboard shortcut is required.");
        var parts = chord.Split('+', StringSplitOptions.TrimEntries)
            .Select(Normalize).ToList();
        if (parts.Count == 1 && Modifiers.Contains(parts[0])) return new KeyboardChord([], parts[0]);
        var modifiers = parts.Where(Modifiers.Contains).Distinct(StringComparer.Ordinal).ToList();
        var keys = parts.Where(x => !Modifiers.Contains(x)).ToList();
        if (keys.Count != 1 || parts.Count != modifiers.Count + 1 || !IsKey(keys[0]))
            throw new ArgumentException("Use one key with optional CTRL, ALT, SHIFT or WIN modifiers, for example CTRL+S or ALT+TAB.");
        return new KeyboardChord(modifiers, keys[0]);
    }

    static string Normalize(string value) => value.Trim().ToUpperInvariant() switch
    {
        "CONTROL" => "CTRL", "WINDOWS" or "META" or "CMD" or "COMMAND" => "WIN", "OPTION" or "OPT" => "ALT", "ESC" => "ESCAPE",
        "RETURN" or "ENTRÉE" or "ENTREE" => "ENTER", "DEL" => "DELETE", "INS" => "INSERT", "PGUP" => "PAGEUP", "PGDN" => "PAGEDOWN",
        var normalized => normalized
    };

    static bool IsKey(string key) =>
        key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) ||
        key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var function) && function is >= 1 and <= 24 ||
        NamedKeys.Contains(key);
}

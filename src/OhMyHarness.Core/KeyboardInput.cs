namespace OhMyHarness.Core;

public sealed record KeyboardChord(IReadOnlyList<string> Modifiers, string Key);

public static class KeyboardInput
{
    static readonly HashSet<string> Modifiers = ["CTRL", "ALT", "SHIFT", "WIN"];
    static readonly HashSet<string> NamedKeys = [
        "ENTER", "TAB", "ESCAPE", "SPACE", "BACKSPACE", "DELETE", "INSERT", "HOME", "END",
        "PAGEUP", "PAGEDOWN", "LEFT", "RIGHT", "UP", "DOWN", "PRINTSCREEN", "CAPSLOCK", "PLUS", "MINUS"
    ];

    public static KeyboardChord ParseChord(string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord)) throw new ArgumentException("A keyboard shortcut is required.");
        var parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).ToList();
        var modifiers = parts.Where(Modifiers.Contains).Distinct(StringComparer.Ordinal).ToList();
        var keys = parts.Where(x => !Modifiers.Contains(x)).ToList();
        if (keys.Count != 1 || parts.Count != modifiers.Count + 1 || !IsKey(keys[0]))
            throw new ArgumentException("Use one key with optional CTRL, ALT, SHIFT or WIN modifiers, for example CTRL+S or ALT+TAB.");
        return new KeyboardChord(modifiers, keys[0]);
    }

    static string Normalize(string value) => value.Trim().ToUpperInvariant() switch
    {
        "CONTROL" => "CTRL", "WINDOWS" or "META" or "CMD" => "WIN", "ESC" => "ESCAPE",
        "RETURN" => "ENTER", "DEL" => "DELETE", "INS" => "INSERT", "PGUP" => "PAGEUP", "PGDN" => "PAGEDOWN",
        var normalized => normalized
    };

    static bool IsKey(string key) =>
        key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) ||
        key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var function) && function is >= 1 and <= 24 ||
        NamedKeys.Contains(key);
}

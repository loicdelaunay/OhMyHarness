using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace OhMyHarness.Cli;

/// <summary>Opt-in Windows Terminal fragments; never rewrites the user's settings.json.</summary>
public static class TerminalProfiles
{
    public static string QuoteArgument(string value)
    {
        if (value.Any(char.IsControl)) throw new ArgumentException("Control characters are not supported in launch paths.");
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); text.Append(c); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
    public static JsonObject Build(CliTheme theme, string executable, string database, string directory, string? assembly = null)
    {
        var args = new List<string> { executable };
        if (assembly != null) args.Add(assembly);
        args.AddRange(["--theme", theme.Id, "--database", Path.GetFullPath(database), "--project", Path.GetFullPath(directory)]);
        var id = SHA256.HashData(Encoding.UTF8.GetBytes(executable + "\n" + database + "\n" + theme.Id));
        var p = theme.Palette;
        return new JsonObject { ["profiles"] = new JsonArray(new JsonObject {
            ["guid"] = new Guid(id.AsSpan(0, 16)).ToString("B"), ["name"] = "OhMyHarness · " + theme.Name,
            ["commandline"] = string.Join(' ', args.Select(QuoteArgument)), ["startingDirectory"] = Path.GetFullPath(directory),
            ["font"] = new JsonObject { ["face"] = theme.Font, ["size"] = theme.FontSize, ["weight"] = theme.Crt ? "semi-bold" : "normal" },
            ["background"] = $"#{p.Background:X6}", ["foreground"] = $"#{p.Foreground:X6}", ["cursorColor"] = $"#{p.Accent:X6}",
            ["selectionBackground"] = $"#{p.Card:X6}", ["cursorShape"] = theme.Crt ? "filledBox" : "bar",
            ["experimental.retroTerminalEffect"] = theme.Crt, ["useAcrylic"] = false, ["opacity"] = 100,
            ["antialiasingMode"] = theme.Crt ? "aliased" : "cleartype", ["hidden"] = false
        }) };
    }
    public static async Task<string> SaveAsync(JsonObject fragment, string folder, CancellationToken ct = default)
    {
        var id = Guid.Parse(fragment["profiles"]![0]!["guid"]!.GetValue<string>());
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "omh-" + id.ToString("N") + ".json");
        await File.WriteAllTextAsync(path, fragment.ToJsonString(new() { WriteIndented = true }), new UTF8Encoding(false), ct);
        return path;
    }
}

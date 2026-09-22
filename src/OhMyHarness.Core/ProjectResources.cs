using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class ProjectResources
{
    public static Project Effective(Chat chat, Project project)
    {
        var copy = new Project { Id = project.Id, Name = project.Name, PermissionProfileJson = project.PermissionProfileJson };
        copy.SetSourceFolders(For(chat, project)); return copy;
    }
    public static List<string> Validate(IEnumerable<string> paths)
    {
        var result = paths.Where(x => !string.IsNullOrWhiteSpace(x)).Select(LocalPreview.ValidatePath).Distinct(PlatformSupport.PathComparer).ToList();
        foreach (var path in result) { if (!Path.Exists(path)) throw new FileNotFoundException("Ressource introuvable : " + path); SandboxWorkspace.AssertNoLinks(path); }
        return result;
    }
    public static bool? AutomaticDecision(string mode, string snapshot, string scope)
    {
        var global = PermissionModes.AutomaticDecision(mode);
        var rule = Decision(snapshot, scope);
        if (global == false || rule == "deny") return false;
        if (rule == "ask") return null;
        return rule == "allow" ? true : global;
    }
    public static async Task DemandToolAsync(Project project, string tool, string details, Func<string,string,CancellationToken,Task<bool>> approve, CancellationToken ct)
    {
        var decision = Decision(project.PermissionProfileJson, tool);
        if (decision == "deny" || decision == "ask" && !await approve(tool, details, ct))
            throw new UnauthorizedAccessException("Outil refusé par les permissions du projet : " + tool);
    }
    public static List<string> For(Chat chat, Project project) => string.IsNullOrEmpty(chat.ResourcePathsJson) ? project.GetSourceFolders() : JsonSerializer.Deserialize<List<string>>(chat.ResourcePathsJson) ?? [];
    public static string Serialize(IEnumerable<string> paths) => JsonSerializer.Serialize(paths.Select(Path.GetFullPath).Distinct(PlatformSupport.PathComparer).ToArray());
    public static async Task<string> ReadPermissionsAsync(Project project, CancellationToken ct = default)
    {
        var combined = new JsonObject();
        foreach (var root in project.GetSourceFolders().Where(Directory.Exists))
        {
            var path = Path.Combine(root, "permission.json"); if (!File.Exists(path)) continue;
            SandboxWorkspace.AssertNoLinks(path);
            if (new FileInfo(path).Length > 32000) throw new IOException("permission.json : 32 Ko maximum.");
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) as JsonObject ?? throw new ArgumentException("permission.json doit contenir un objet.");
            if (json["permissions"] is not JsonObject rules) throw new ArgumentException("permission.json : objet permissions requis.");
            foreach (var rule in rules)
            {
                var value = rule.Value?.GetValue<string>();
                if (value is not ("allow" or "ask" or "deny") || string.IsNullOrWhiteSpace(rule.Key) || rule.Key.Length > 2048) throw new ArgumentException("Permission invalide : " + rule.Key);
                // Deny wins when multiple project roots disagree.
                if (combined[rule.Key]?.GetValue<string>() != "deny") combined[rule.Key] = value;
            }
        }
        return combined.Count == 0 ? "" : combined.ToJsonString();
    }
    // Use only the profile explicitly imported by the user, never live agent-editable file contents.
    public static string? Decision(string snapshot, string scope)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        var rules = JsonNode.Parse(snapshot)!.AsObject();
        var type = scope.Split('|')[0];
        return (rules[scope] ?? rules[type] ?? rules["*"])?.GetValue<string>();
    }
}

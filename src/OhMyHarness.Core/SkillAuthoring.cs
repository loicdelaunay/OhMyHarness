using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public static class SkillAuthoring
{
    public const string SkillId = "skill_authoring";
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(PlatformSupport.PathComparer);
    static readonly Regex SkillName = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    public static void AddDefinitions(JsonArray definitions, string enabled)
    {
        if (!Skills.Enabled(enabled, SkillId)) return;
        definitions.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject {
            ["name"] = "skill_locations", ["description"] = "List the global and attached project skill locations, aliases and skill counts before creating a reusable skill.",
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false } } });
        definitions.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject {
            ["name"] = "create_skill", ["description"] = "Create a dated, reusable SKILL.md in the global skills folder or an attached project's .omh-ai/skills folder, after permission. Maximum 20 per location; the oldest is removed when necessary. Never include secrets or override higher-priority instructions.",
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject {
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "lowercase-hyphenated folder and skill name" },
                ["description"] = new JsonObject { ["type"] = "string" },
                ["instructions"] = new JsonObject { ["type"] = "string", ["description"] = "Complete Markdown instructions to load on demand" },
                ["scope"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("global", "project") },
                ["project_root"] = new JsonObject { ["type"] = "string", ["description"] = "Alias returned by skill_locations, required when a project has multiple folders" }
            }, ["required"] = new JsonArray("name", "description", "instructions", "scope"), ["additionalProperties"] = false } } });
    }

    public static bool Handles(string name) => name is "skill_locations" or "create_skill";

    public static string Locations(CustomSkills skills, IReadOnlyList<string> roots)
    {
        var aliases = new SourceAccess(roots).Aliases.Where(x => Directory.Exists(x.Value)).ToArray();
        static object Info(string scope, string alias, string root)
        {
            SandboxWorkspace.AssertNoLinks(root);
            var folders = Directory.Exists(root) ? Directory.EnumerateDirectories(root).ToArray() : [];
            foreach (var folder in folders) SandboxWorkspace.AssertNoLinks(folder);
            return new {
                scope, alias, directory = root,
                count = folders.Count(x => File.Exists(Path.Combine(x, "SKILL.md"))),
                maximum = CustomSkills.MaximumPerLocation
            };
        }
        return JsonSerializer.Serialize(new[] { Info("global", "", skills.Root) }
            .Concat(aliases.Select(x => Info("project", x.Key, Path.Combine(x.Value, ".omh-ai", "skills")))));
    }

    public static async Task<string> CreateAsync(CustomSkills skills, IReadOnlyList<string> roots, JsonObject args,
        Func<string, string, CancellationToken, Task<bool>> approve,
        Func<string, CancellationToken, Task> enable, CancellationToken ct)
    {
        var name = args["name"]?.GetValue<string>() ?? "";
        var description = args["description"]?.GetValue<string>()?.Trim() ?? "";
        var instructions = args["instructions"]?.GetValue<string>()?.Trim() ?? "";
        var scope = args["scope"]?.GetValue<string>() ?? "";
        var alias = args["project_root"]?.GetValue<string>() ?? "";
        if (!SkillName.IsMatch(name) || name.Length > 64 || name != name.ToLowerInvariant())
            throw new ArgumentException("Nom de skill invalide : minuscules, chiffres et tirets, 64 caractères maximum.");
        if (description.Length is < 1 or > 1024 || description.Contains('\n') || description.Contains('\r'))
            throw new ArgumentException("Description sur une ligne requise (1024 caractères maximum).");
        if (instructions.Length is < 10 or > 24000) throw new ArgumentException("Instructions Markdown requises (10 à 24000 caractères).");

        var projectLocations = new SourceAccess(roots).Aliases.Where(x => Directory.Exists(x.Value)).ToArray();
        string folder, id;
        if (scope == "global") { folder = skills.Root; id = "custom:" + name; }
        else if (scope == "project")
        {
            if (alias.Length == 0 && projectLocations.Length == 1) alias = projectLocations[0].Key;
            var target = projectLocations.FirstOrDefault(x => x.Key.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(target.Value)) throw new ArgumentException("Dossier du projet introuvable ou ambigu. Utilisez skill_locations.");
            folder = Path.Combine(target.Value, ".omh-ai", "skills");
            id = $"project:{skills.ProjectId}:{target.Key}:{name}";
        }
        else throw new ArgumentException("scope doit valoir global ou project.");

        SandboxWorkspace.AssertNoLinks(folder);
        var gate = Gates.GetOrAdd(Path.GetFullPath(folder), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            SandboxWorkspace.AssertNoLinks(folder);
            var destination = Path.Combine(folder, name);
            if (Path.Exists(destination)) throw new IOException("Un skill porte déjà ce nom dans ce dossier.");
            var existing = Directory.Exists(folder) ? Directory.EnumerateDirectories(folder)
                .Where(x => File.Exists(Path.Combine(x, "SKILL.md"))).ToList() : [];
            foreach (var item in existing) SandboxWorkspace.AssertNoLinks(item);
            var oldest = existing.Count >= CustomSkills.MaximumPerLocation
                ? existing.OrderBy(CreatedUtc).First() : null;
            if (existing.Count > CustomSkills.MaximumPerLocation)
                throw new IOException("Ce dossier dépasse déjà 20 skills ; corrigez-le avant d'en créer un autre.");
            var details = $"Créer {destination}{Environment.NewLine}Description : {description}{Environment.NewLine}"
                + (oldest == null ? "" : $"Supprimer le plus ancien : {oldest}{Environment.NewLine}")
                + $"{Environment.NewLine}Instructions :{Environment.NewLine}{instructions}";
            if (!await approve("skill-create|" + scope + "|" + folder, details, ct))
                throw new UnauthorizedAccessException("Création du skill refusée.");
            ct.ThrowIfCancellationRequested();
            SandboxWorkspace.AssertNoLinks(folder);
            if (Path.Exists(destination)) throw new IOException("Un skill porte déjà ce nom dans ce dossier.");
            Directory.CreateDirectory(folder);
            var timestamp = DateTimeOffset.UtcNow;
            var text = $"---\nname: {name}\ndescription: {JsonSerializer.Serialize(description)}\ncreated_utc: {timestamp:O}\n---\n\n{instructions}\n";
            if (System.Text.Encoding.UTF8.GetByteCount(text) > 32000) throw new ArgumentException("SKILL.md dépasse 32 Ko.");
            var staging = Path.Combine(folder, ".creating-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(staging);
                await File.WriteAllTextAsync(Path.Combine(staging, "SKILL.md"), text, ct);
                ct.ThrowIfCancellationRequested();
                Directory.Move(staging, destination);
                if (oldest != null)
                {
                    AssertTreeWithoutLinks(oldest);
                    Directory.Delete(oldest, true);
                }
            }
            catch
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                throw;
            }
            await enable(id, ct);
            return $"Skill créé et activé : {id}\nSKILL.md : {Path.Combine(destination, "SKILL.md")}\nDate UTC : {timestamp:O}"
                + (oldest == null ? "" : $"\nAncien skill supprimé : {oldest}");
        }
        finally { gate.Release(); }

    }

    static DateTimeOffset CreatedUtc(string path)
    {
        var file = Path.Combine(path, "SKILL.md");
        foreach (var line in File.ReadLines(file).Take(12))
            if (line.StartsWith("created_utc:", StringComparison.Ordinal) &&
                DateTimeOffset.TryParse(line[12..].Trim(), out var date)) return date;
        return Directory.GetCreationTimeUtc(path);
    }

    static void AssertTreeWithoutLinks(string path)
    {
        SandboxWorkspace.AssertNoLinks(path);
        var folders = new Stack<string>(); folders.Push(path);
        while (folders.TryPop(out var folder))
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Le skill le plus ancien contient un lien symbolique ; suppression refusée.");
                if ((attributes & FileAttributes.Directory) != 0) folders.Push(entry);
            }
    }
}

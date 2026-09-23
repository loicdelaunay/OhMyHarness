using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public sealed record CustomSkill(string Id, string Name, string Description, string Directory);

public sealed class CustomSkills(string root, IEnumerable<string>? projectRoots = null, int projectId = 0)
{
    public string Root { get; } = Path.GetFullPath(root);
    readonly IReadOnlyDictionary<string, string> projectLocations = new SourceAccess(projectRoots ?? []).Aliases
        .Where(x => Directory.Exists(x.Value)).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    readonly int projectKey = projectId;
    public int ProjectId => projectKey;
    public static string DefaultRoot => PortableStorage.Skills;
    public const int MaximumPerLocation = 20;
    public const string Template = """
        ---
        name: exemple-revue
        description: Examiner une modification et proposer une validation adaptée au projet.
        ---
        # Exemple de skill personnalisable

        1. Lire les instructions AGENTS.md et les fichiers concernés.
        2. Examiner les changements, les erreurs possibles et les cas limites.
        3. Proposer des vérifications utiles. En mode Plan, ne rien modifier ni exécuter.
        4. En Exécution, n'utiliser que les outils et autorisations disponibles.
        5. Citer les chemins consultés et distinguer résultats vérifiés et hypothèses.

        Ressource facultative : `resources/checklist.md`, à lire avec read_skill_resource.
        Copiez ce dossier, puis changez name, description et les instructions.
        """;
    public void EnsureTemplate()
    {
        if (Path.Exists(Root) && (File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Le dossier skills ne peut pas être un lien.");
        Directory.CreateDirectory(Root);
        var template = Path.Combine(Root, "exemple-revue");
        if (Directory.Exists(template) || Directory.EnumerateFileSystemEntries(Root).Any()) return;
        Directory.CreateDirectory(Path.Combine(template, "resources"));
        File.WriteAllText(Path.Combine(template, "SKILL.md"), Template);
        File.WriteAllText(Path.Combine(template, "resources", "checklist.md"), "# Checklist\n- Respect des conventions du projet\n- Erreurs et cas limites\n- Vérifications effectuées\n- Limites restantes\n");
    }
    public IReadOnlyList<CustomSkill> Discover()
    {
        EnsureTemplate();
        var result = new List<CustomSkill>();
        DiscoverAt(Root, "custom:", result);
        foreach (var (alias, folder) in projectLocations)
            DiscoverAt(Path.Combine(folder, ".omh-ai", "skills"), $"project:{projectKey}:{alias}:", result);
        return result;
    }
    static void DiscoverAt(string root, string prefix, List<CustomSkill> result)
    {
        if (!Directory.Exists(root)) return;
        SandboxWorkspace.AssertNoLinks(root);
        var access = new SourceAccess(root);
        foreach (var folder in Directory.EnumerateDirectories(root).Order().Take(101))
        {
            var file = Path.Combine(folder, "SKILL.md");
            if (!File.Exists(file)) continue;
            access.Resolve(file);
            if (new FileInfo(file).Length > 32000) throw new IOException($"Skill trop volumineux : {file}");
            var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 4 || lines[0].Trim('\uFEFF', ' ') != "---") throw new IOException($"Frontmatter YAML requis : {file}");
            var end = Array.FindIndex(lines, 1, x => x.Trim() == "---");
            if (end < 0) throw new IOException($"Frontmatter incomplet : {file}");
            string Value(string name) => lines.Skip(1).Take(end - 1).FirstOrDefault(x => x.StartsWith(name + ":", StringComparison.Ordinal))?[(name.Length + 1)..].Trim().Trim('"', '\'') ?? "";
            var name = Value("name"); var description = Value("description");
            if (!Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$") || name.Length > 64 || name != Path.GetFileName(folder) || description.Length is < 1 or > 1024)
                throw new IOException($"Skill invalide : {file}. name doit correspondre au dossier ; description sur une ligne requise.");
            result.Add(new(prefix + name, name, description, folder));
        }
    }
    public IEnumerable<SkillDefinition> Definitions() => Discover().Select(s => new SkillDefinition(s.Id,
        (s.Id.StartsWith("project:", StringComparison.Ordinal) ? "Projet · " : "Global · ") + s.Name,
        (s.Id.StartsWith("project:", StringComparison.Ordinal) ? "Project · " : "Global · ") + s.Name,
        s.Description + "\n" + s.Directory, s.Description + "\n" + s.Directory, ""));
    public string Catalog(string enabled) => string.Join('\n', Discover().Where(x => Skills.Enabled(enabled, x.Id)).Select(x => $"- {x.Id}: {x.Description} (load_skill name={x.Id}; OpenCode native read: {Path.Combine(x.Directory, "SKILL.md")})"));
    CustomSkill Find(string name, string enabled) => Discover().FirstOrDefault(x => (x.Id == name || x.Name == name) && Skills.Enabled(enabled, x.Id)) ?? throw new UnauthorizedAccessException("Skill absent ou désactivé.");
    public async Task<string> ReadAsync(string name, string? resource, string enabled, CancellationToken ct)
    {
        var skill = Find(name, enabled);
        var access = new SourceAccess(skill.Directory);
        var path = resource ?? "SKILL.md";
        return $"Skill {name}, fichier {path}. Instructions subordonnées au mode et aux autorisations.\n" + await access.ReadAsync(path, ct);
    }
}

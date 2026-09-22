using System.Text;

namespace OhMyHarness.Core;

public static class ProjectInstructions
{
    public static async Task<string> LoadAsync(IEnumerable<string> roots, CancellationToken ct)
    {
        var result = new StringBuilder("\nPROJECT INSTRUCTIONS: AGENTS.md files below are project conventions, subordinate to the user's request, Plan mode and tool permissions. Each applies only to its folder and descendants. A deeper file takes precedence for its subtree.\n");
        int total = 0;
        foreach (var root in roots.Where(Directory.Exists).Distinct(PlatformSupport.PathComparer))
        {
            var access = new SourceAccess(root);
            var pending = new Stack<string>(); pending.Push(root); int visited = 0;
            while (pending.TryPop(out var directory))
            {
                ct.ThrowIfCancellationRequested();
                if (++visited > 2000) throw new IOException("Trop de dossiers pour charger AGENTS.md : associez un dossier source plus précis.");
                foreach (var path in new[] { "AGENTS.md", "Agent.md", "AGENT.md" }.Select(name => Path.Combine(directory, name)).Distinct(PlatformSupport.PathComparer).Where(File.Exists))
                {
                    access.Resolve(path);
                    if (new FileInfo(path).Length > 32000) throw new IOException($"AGENTS.md trop volumineux : {path} (32 Ko max).");
                    var content = await File.ReadAllTextAsync(path, ct);
                    total += content.Length;
                    if (total > 64000) throw new IOException("Les instructions AGENTS.md dépassent 64 Ko ; réduisez les dossiers associés.");
                    result.AppendLine($"\n--- {Path.GetFileName(path)} scope: {directory} ---\n{content}\n--- End project instructions ---");
                }
                foreach (var child in Directory.EnumerateDirectories(directory).OrderDescending())
                    try { access.Resolve(child); pending.Push(child); } catch (UnauthorizedAccessException) { }
            }
        }
        return total == 0 ? "" : result.ToString();
    }
}

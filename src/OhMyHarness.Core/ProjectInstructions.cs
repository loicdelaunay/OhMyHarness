using System.Text;

namespace OhMyHarness.Core;

public static class ProjectInstructions
{
    public static Task<string> LoadAsync(IEnumerable<string> roots, CancellationToken ct)
    {
        var folders = roots.ToArray();
        // Directory enumeration itself is synchronous: keep all filesystem work off the UI thread.
        return Task.Run(() => ScanAsync(folders, ct), ct);
    }
    static async Task<string> ScanAsync(IEnumerable<string> roots, CancellationToken ct)
    {
        var result = new StringBuilder("\nPROJECT INSTRUCTIONS: AGENTS.md files below are project conventions, subordinate to the user's request, Plan mode and tool permissions. Each applies only to its folder and descendants. A deeper file takes precedence for its subtree.\n");
        int total = 0;
        foreach (var root in roots.Where(Directory.Exists).Distinct(PlatformSupport.PathComparer))
        {
            var access = new SourceAccess(root);
            var pending = new Queue<string>(); pending.Enqueue(root);
            while (pending.TryDequeue(out var directory))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    access.Resolve(directory);
                    foreach (var path in Directory.EnumerateFiles(directory).Where(p => Path.GetFileName(p).Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).Equals("agent.md", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
                    {
                        try
                        {
                            access.Resolve(path);
                            if (new FileInfo(path).Length > 32000) continue;
                            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                            if (total + content.Length > 64000) continue;
                            total += content.Length;
                            result.AppendLine($"\n--- {Path.GetFileName(path)} scope: {directory} ---\n{content}\n--- End project instructions ---");
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                    foreach (var child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
                    {
                        if (Path.GetFileName(child) is "artifacts" or "runtimes" or "logs" or ".venv" or "venv") continue;
                        try { access.Resolve(child); pending.Enqueue(child); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return total == 0 ? "" : result.ToString();
    }
}

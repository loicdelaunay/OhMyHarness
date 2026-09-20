namespace OhMyHarness.Core;

public sealed record GitChangedFile(string Repository, string Path, string Status, string? PreviousPath = null)
{
    public override string ToString() => $"{Status.Trim()}  {Path}  · {System.IO.Path.GetFileName(Repository)}";
}

public static class GitWorkspace
{
    static async Task<string> Run(string root, IEnumerable<string> args, CancellationToken ct, bool diff = false)
    {
        var result = await WorkspaceTools.GitAsync(root, args, ct);
        if (!result.StartsWith("Exit code: 0\n") && !(diff && result.StartsWith("Exit code: 1\n")))
            throw new IOException(result);
        if (result.Contains("[output truncated]")) throw new IOException("Diff ou liste trop volumineux (100 Ko maximum).");
        return result[(result.IndexOf('\n') + 1)..].TrimEnd('\r', '\n');
    }

    public static async Task<List<GitChangedFile>> ListAsync(IEnumerable<string> roots, CancellationToken ct)
    {
        var files = new List<GitChangedFile>();
        foreach (var root in roots.Where(WorkspaceTools.HasGitRepository).Distinct(PlatformSupport.PathComparer))
        {
            var output = await Run(root, ["--no-pager", "status", "--porcelain=v1", "-z", "--untracked-files=all"], ct);
            var entries = output.Split('\0');
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i]; if (entry.Length < 4) continue;
                var status = entry[..2]; var path = entry[3..]; string? previous = null;
                if (status.Contains('R') || status.Contains('C'))
                {
                    if (++i >= entries.Length) throw new IOException("Statut Git incomplet.");
                    previous = entries[i];
                }
                files.Add(new(root, path, status, previous));
            }
        }
        return files;
    }

    public static async Task<string> DiffAsync(GitChangedFile file, CancellationToken ct)
    {
        // Re-read status: only files reported by Git in the attached repository can be requested.
        var current = (await ListAsync([file.Repository], ct)).FirstOrDefault(x => x.Path == file.Path);
        if (current is null) return "Aucune modification / No changes.";
        var head = await WorkspaceTools.GitAsync(file.Repository, ["rev-parse", "--verify", "HEAD"], ct);
        if (current.Status == "??" || !head.StartsWith("Exit code: 0\n"))
        {
            var full = new SourceAccess(file.Repository).Resolve(current.Path);
            if (!File.Exists(full)) return "Fichier absent / Missing file.";
            return await Run(file.Repository, ["--no-pager", "diff", "--no-index", "--no-ext-diff", "--no-textconv", "--no-color", "--", "/dev/null", full], ct, true);
        }
        var args = new List<string> { "--no-pager", "--literal-pathspecs", "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=3", "HEAD", "--", current.Path };
        if (current.PreviousPath != null) args.Add(current.PreviousPath);
        var result = await Run(file.Repository, args, ct);
        return string.IsNullOrEmpty(result) ? "Aucune différence nette avec HEAD / No net difference from HEAD." : result;
    }
}

namespace OhMyHarness.Core;

public sealed record GitChangedFile(string Repository, string Path, string Status, string? PreviousPath = null)
{
    public int? Added { get; init; }
    public int? Removed { get; init; }
    public string? PreviewNotice { get; init; }
    public override string ToString() => $"{Status.Trim()}  {Path}  · {System.IO.Path.GetFileName(Repository)}";
}

public static class GitWorkspace
{
    public static async Task<List<GitChangedFile>> ListWithStatsAsync(IEnumerable<string> roots, CancellationToken ct)
    {
        var files = await ListAsync(roots, ct);
        for (var i = 0; i < files.Count; i++)
        {
            try
            {
                var preview = ParseDiff(await DiffKnownAsync(files[i], ct));
                files[i] = files[i] with { Added = preview.Rows.Count(x => x.Kind == "added"), Removed = preview.Rows.Count(x => x.Kind == "removed"), PreviewNotice = preview.Notice };
                if (preview.Notice != null) files[i] = files[i] with { Added = null, Removed = null };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { files[i] = files[i] with { PreviewNotice = ex.Message }; }
        }
        return files;
    }

    public sealed record DiffRow(int? BeforeLine, string? Before, int? AfterLine, string? After, string Kind);
    public sealed record DiffPreview(List<DiffRow> Rows, string? Notice);
    public static DiffPreview ParseDiff(string diff)
    {
        var rows = new List<DiffRow>(); int before = 0, after = 0; bool inHunk = false;
        foreach (var raw in diff.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var hunk = System.Text.RegularExpressions.Regex.Match(line, @"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@");
            if (hunk.Success)
            {
                before = int.Parse(hunk.Groups[1].Value); after = int.Parse(hunk.Groups[2].Value); inHunk = true;
                rows.Add(new(null, line, null, line, "hunk")); continue;
            }
            if (line.StartsWith("diff --git")) { inHunk = false; continue; }
            if (!inHunk || line.Length == 0) continue;
            if (line[0] == '-') rows.Add(new(before++, line[1..], null, null, "removed"));
            else if (line[0] == '+') rows.Add(new(null, null, after++, line[1..], "added"));
            else if (line[0] == ' ') rows.Add(new(before++, line[1..], after++, line[1..], "context"));
            else if (line[0] == '\\') rows.Add(new(null, line, null, line, "hunk"));
        }
        return new(rows, rows.Count == 0 ? diff : null);
    }
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
        return await DiffKnownAsync(current, ct);
    }

    static async Task<string> DiffKnownAsync(GitChangedFile file, CancellationToken ct)
    {
        var current = file;
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

using System.Text;
using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public sealed partial class SourceAccess
{
    public Task<string> SearchAsync(string glob, string? query, bool regex, bool ignoreCase, CancellationToken ct) => Task.Run(() =>
    {
        if (_roots.Count == 0) throw new InvalidOperationException("Aucun dossier source associé.");
        if (glob.Length > 500 || (query?.Length ?? 0) > 2000) throw new ArgumentException("Motif trop long.");
        if (glob.Contains("..") || Path.IsPathRooted(glob)) throw new ArgumentException("Utilisez un glob relatif aux sources.");
        var pattern = Regex.Escape(glob.Replace('\\', '/')).Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
        var matcher = new Regex("^" + pattern + "$", OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var search = query is null ? null : new Regex(regex ? query : Regex.Escape(query), ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (query == "") throw new ArgumentException("Recherche vide.");
        var result = new StringBuilder(); int visited = 0, hits = 0; bool truncated = false;
        foreach (var root in _roots)
        {
            var directories = new Stack<string>(); directories.Push(root.FullPath);
            while (directories.TryPop(out var directory))
            {
                ct.ThrowIfCancellationRequested();
                ResolveInRoot(root.FullPath, Path.GetRelativePath(root.FullPath, directory));
                foreach (var path in File.Exists(directory) ? new[] { directory } : Directory.EnumerateFileSystemEntries(directory))
                {
                    ct.ThrowIfCancellationRequested();
                    if (++visited > 20000 || hits >= 300 || result.Length >= 60000) { truncated = true; break; }
                    var name = Path.GetFileName(path);
                    if (Excluded.Contains(name) || name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) continue;
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) { directories.Push(path); continue; }
                    var relative = File.Exists(root.FullPath) ? root.Alias : Path.GetRelativePath(root.FullPath, path).Replace('\\', '/');
                    var display = _roots.Count > 1 && !File.Exists(root.FullPath) ? root.Alias + "/" + relative : relative;
                    if (!matcher.IsMatch(relative) && !matcher.IsMatch(display)) continue;
                    if (search is null) { result.AppendLine(display); hits++; continue; }
                    if (!Extensions.Contains(Path.GetExtension(path)) || new FileInfo(path).Length > 128000) continue;
                    var lineNumber = 0;
                    foreach (var line in File.ReadLines(ResolveInRoot(root.FullPath, relative)))
                    {
                        ct.ThrowIfCancellationRequested(); lineNumber++;
                        if (!search.IsMatch(line)) continue;
                        result.AppendLine($"{display}:{lineNumber}: {line[..Math.Min(line.Length, 500)]}"); hits++;
                        if (hits >= 300 || result.Length >= 60000) { truncated = true; break; }
                    }
                }
                if (truncated) break;
            }
            if (truncated) break;
        }
        return result + (truncated ? "\n[Résultats limités : précisez le glob/la recherche.]" : "\n[Recherche terminée. Grep ignore les formats non textuels et les fichiers >128 Ko.]");
    }, ct);
}

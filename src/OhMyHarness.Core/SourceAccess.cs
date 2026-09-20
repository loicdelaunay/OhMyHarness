namespace OhMyHarness.Core;

public sealed partial class SourceAccess
{
    static readonly SemaphoreSlim WriteGate = new(1, 1);
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".csproj", ".sln", ".slnx", ".xaml", ".json", ".md", ".txt", ".ts", ".tsx", ".js", ".jsx", ".cjs", ".mjs", ".css", ".html", ".py", ".dart", ".yaml", ".yml", ".xml", ".sql", ".rs", ".go", ".java", ".cpp", ".h", ".toml", ".swift", ".m", ".mm", ".sh", ".zsh", ".plist", ".entitlements", ".xcconfig", ".ps1", ".bat" };
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".vs", "bin", "obj", "node_modules", ".env", "secrets.json", "appsettings.Production.json", "dist", "build" };

    record RootInfo(string FullPath, string Alias);
    readonly List<RootInfo> _roots = [];

    public SourceAccess(string root) : this(string.IsNullOrWhiteSpace(root) ? [] : [root]) { }

    public SourceAccess(IEnumerable<string> roots)
    {
        var rawList = roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => Path.GetFullPath(r.Trim())).Distinct(PlatformSupport.PathComparer).ToList();
        var aliasCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rawList)
        {
            var baseName = Path.GetFileName(r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "root";
            aliasCounts[baseName] = aliasCounts.TryGetValue(baseName, out var c) ? c + 1 : 1;
        }

        var seenAliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rawList)
        {
            var baseName = Path.GetFileName(r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "root";
            string alias;
            if (aliasCounts[baseName] > 1)
            {
                seenAliases[baseName] = seenAliases.TryGetValue(baseName, out var count) ? count + 1 : 1;
                alias = $"{baseName}_{seenAliases[baseName]}";
            }
            else
            {
                alias = baseName;
            }
            _roots.Add(new RootInfo(r, alias));
        }
    }

    public IReadOnlyList<string> Roots => _roots.Select(r => r.FullPath).ToList();
    public IReadOnlyDictionary<string, string> Aliases => _roots.ToDictionary(r => r.Alias, r => r.FullPath, StringComparer.OrdinalIgnoreCase);

    static string ResolveInRoot(string rootPath, string subpath)
    {
        var basePath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(basePath, subpath));
        if (!full.StartsWith(basePath, PlatformSupport.PathComparison) && full + Path.DirectorySeparatorChar != basePath)
            throw new UnauthorizedAccessException("Chemin hors du projet.");
        var current = basePath.TrimEnd(Path.DirectorySeparatorChar);
        if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Liens symboliques exclus.");
        foreach (var part in Path.GetRelativePath(basePath, full).Split(Path.DirectorySeparatorChar))
        {
            if (Excluded.Contains(part) || part.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Fichier exclu.");
            current = Path.Combine(current, part);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Liens symboliques exclus.");
        }
        return full;
    }

    public string Resolve(string relative)
    {
        if (_roots.Count == 0) throw new InvalidOperationException("Aucun dossier source associé au projet.");
        if (string.IsNullOrWhiteSpace(relative)) relative = ".";

        if (Path.IsPathFullyQualified(relative))
        {
            var fullReq = Path.GetFullPath(relative);
            var match = _roots.FirstOrDefault(r =>
            {
                var bp = r.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return fullReq.StartsWith(bp, PlatformSupport.PathComparison) || string.Equals(fullReq, r.FullPath, PlatformSupport.PathComparison);
            });
            if (match != null)
            {
                var sub = Path.GetRelativePath(match.FullPath, fullReq);
                return ResolveInRoot(match.FullPath, sub);
            }
            throw new UnauthorizedAccessException("Chemin hors du projet.");
        }

        var normalized = relative.Replace('/', Path.DirectorySeparatorChar);

        if (_roots.Count == 1)
        {
            var single = _roots[0];
            var subpath = normalized;
            if (string.Equals(subpath, single.Alias, StringComparison.OrdinalIgnoreCase))
                subpath = ".";
            else if (subpath.StartsWith(single.Alias + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                subpath = subpath[(single.Alias.Length + 1)..];
            return ResolveInRoot(single.FullPath, subpath);
        }

        foreach (var r in _roots)
        {
            if (string.Equals(normalized, r.Alias, StringComparison.OrdinalIgnoreCase))
                return ResolveInRoot(r.FullPath, ".");
            if (normalized.StartsWith(r.Alias + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var subpath = normalized[(r.Alias.Length + 1)..];
                return ResolveInRoot(r.FullPath, subpath);
            }
        }

        if (normalized == ".")
            throw new InvalidOperationException($"Plusieurs dossiers sources sont associés ({string.Join(", ", _roots.Select(r => r.Alias))}). Utilisez 'list_sources' pour les parcourir ou préfixez le chemin avec le nom du dossier source.");

        var matchingRoots = new List<RootInfo>();
        foreach (var r in _roots)
        {
            try
            {
                var candidate = ResolveInRoot(r.FullPath, normalized);
                if (File.Exists(candidate) || Directory.Exists(candidate))
                    matchingRoots.Add(r);
            }
            catch (UnauthorizedAccessException) { }
        }

        if (matchingRoots.Count == 1)
        {
            return ResolveInRoot(matchingRoots[0].FullPath, normalized);
        }
        if (matchingRoots.Count > 1)
        {
            var aliases = string.Join(", ", matchingRoots.Select(m => m.Alias));
            throw new InvalidOperationException($"Le chemin '{relative}' est présent dans plusieurs dossiers sources ({aliases}). Veuillez préfixer le chemin avec le nom du dossier souhaité (ex: '{matchingRoots[0].Alias}/{relative}').");
        }

        throw new InvalidOperationException($"Plusieurs dossiers sources sont associés ({string.Join(", ", _roots.Select(r => r.Alias))}). Veuillez préfixer le chemin avec le nom du dossier source cible (ex: '{_roots[0].Alias}/{relative}').");
    }

    public string List(string relative = ".")
    {
        if (_roots.Count == 0) throw new InvalidOperationException("Aucun dossier source associé au projet.");
        if (string.IsNullOrWhiteSpace(relative)) relative = ".";

        if (_roots.Count > 1 && (relative == "." || relative == ""))
        {
            return string.Join('\n', _roots.Select(r => $"[dossier] {r.Alias}"));
        }

        if (_roots.Count == 1)
        {
            var root = _roots[0];
            var subpath = relative.Replace('/', Path.DirectorySeparatorChar);
            if (string.Equals(subpath, root.Alias, StringComparison.OrdinalIgnoreCase)) subpath = ".";
            else if (subpath.StartsWith(root.Alias + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                subpath = subpath[(root.Alias.Length + 1)..];

            var directory = ResolveInRoot(root.FullPath, subpath);
            return string.Join('\n', Directory.EnumerateFileSystemEntries(directory)
                .Where(p => !Excluded.Contains(Path.GetFileName(p)) && !Path.GetFileName(p).StartsWith(".env", StringComparison.OrdinalIgnoreCase))
                .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
                .Take(300).Select(p => (Directory.Exists(p) ? "[dossier] " : "") + Path.GetRelativePath(root.FullPath, p).Replace('\\', '/')));
        }

        var norm = relative.Replace('/', Path.DirectorySeparatorChar);
        RootInfo? matchedRoot = null;
        string relInRoot = norm;
        foreach (var r in _roots)
        {
            if (string.Equals(norm, r.Alias, StringComparison.OrdinalIgnoreCase))
            {
                matchedRoot = r;
                relInRoot = ".";
                break;
            }
            if (norm.StartsWith(r.Alias + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                matchedRoot = r;
                relInRoot = norm[(r.Alias.Length + 1)..];
                break;
            }
        }

        if (matchedRoot == null)
        {
            var candidateRoots = _roots.Where(r => Directory.Exists(Path.Combine(r.FullPath, norm))).ToList();
            if (candidateRoots.Count == 1)
            {
                matchedRoot = candidateRoots[0];
                relInRoot = norm;
            }
            else if (candidateRoots.Count > 1)
            {
                throw new InvalidOperationException($"Le dossier '{relative}' existe dans plusieurs dossiers sources ({string.Join(", ", candidateRoots.Select(c => c.Alias))}). Précisez le nom du dossier source.");
            }
            else
            {
                throw new DirectoryNotFoundException($"Dossier '{relative}' introuvable dans les dossiers sources.");
            }
        }

        var targetDir = ResolveInRoot(matchedRoot.FullPath, relInRoot);
        return string.Join('\n', Directory.EnumerateFileSystemEntries(targetDir)
            .Where(p => !Excluded.Contains(Path.GetFileName(p)) && !Path.GetFileName(p).StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
            .Take(300).Select(p =>
            {
                var relPath = Path.GetRelativePath(matchedRoot.FullPath, p).Replace('\\', '/');
                return (Directory.Exists(p) ? "[dossier] " : "") + matchedRoot.Alias + "/" + relPath;
            }));
    }

    public async Task<string> ReadAsync(string relative, CancellationToken ct, int? startLine = null, int? endLine = null)
    {
        if (startLine.HasValue != endLine.HasValue || startLine is < 1 || endLine < startLine || (long?)endLine - startLine >= 2000)
            throw new ArgumentException("Indiquez start_line et end_line ensemble : lignes à partir de 1, bornes incluses, 2 000 lignes maximum.");
        var full = Resolve(relative);
        if (!Extensions.Contains(Path.GetExtension(full))) throw new InvalidOperationException("Format source non pris en charge.");
        if (startLine.HasValue)
        {
            if (new FileInfo(full).Length > 16 * 1024 * 1024) throw new InvalidOperationException("Lecture partielle : fichier >16 Mio.");
            using var reader = new StreamReader(full);
            var result = new System.Text.StringBuilder();
            int lineNumber = 0;
            while (lineNumber < endLine && await reader.ReadLineAsync(ct) is { } line)
            {
                lineNumber++;
                if (lineNumber < startLine) continue;
                if ((long)result.Length + line.Length + 24 > 128000)
                    throw new InvalidOperationException("Extrait trop volumineux (128 000 caractères maximum). Demandez une plage plus courte.");
                result.Append(lineNumber).Append(": ").Append(line).Append('\n');
            }
            if (lineNumber < startLine) return $"Plage hors fichier : {lineNumber} ligne(s), début demandé : {startLine}.";
            return $"{relative} — lignes {startLine} à {lineNumber} (incluses)\n" + result;
        }
        if (new FileInfo(full).Length > 128_000) throw new InvalidOperationException("Fichier trop volumineux (128 Ko maximum).");
        return await File.ReadAllTextAsync(full, ct);
    }

    public async Task<string> WriteAsync(string relative, string content, CancellationToken ct)
    {
        await WriteGate.WaitAsync(ct);
        try { return await WriteCoreAsync(relative, content, ct); }
        finally { WriteGate.Release(); }
    }

    async Task<string> WriteCoreAsync(string relative, string content, CancellationToken ct)
    {
        var full = Resolve(relative);
        var ext = Path.GetExtension(full);
        if (string.IsNullOrEmpty(ext) || !Extensions.Contains(ext))
            throw new InvalidOperationException("Format source non pris en charge.");
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(full, content, ct);
        return $"Fichier '{relative}' écrit avec succès ({content.Length} caractères).";
    }

    public async Task<string> ModifyAsync(string relative, string oldText, string newText, CancellationToken ct)
    {
        await WriteGate.WaitAsync(ct);
        try { return await ModifyCoreAsync(relative, oldText, newText, ct); }
        finally { WriteGate.Release(); }
    }

    async Task<string> ModifyCoreAsync(string relative, string oldText, string newText, CancellationToken ct)
    {
        var full = Resolve(relative);
        var ext = Path.GetExtension(full);
        if (string.IsNullOrEmpty(ext) || !Extensions.Contains(ext))
            throw new InvalidOperationException("Format source non pris en charge.");
        if (!File.Exists(full))
            throw new FileNotFoundException($"Le fichier '{relative}' n'existe pas.");
        var current = await File.ReadAllTextAsync(full, ct);
        var index = current.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException($"Le texte cible à remplacer est introuvable dans '{relative}'.");
        var updated = current.Remove(index, oldText.Length).Insert(index, newText);
        await File.WriteAllTextAsync(full, updated, ct);
        return $"Fichier '{relative}' modifié avec succès.";
    }
}

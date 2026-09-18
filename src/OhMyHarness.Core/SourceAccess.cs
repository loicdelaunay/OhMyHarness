namespace OhMyHarness.Core;

public sealed class SourceAccess(string root)
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".cs", ".csproj", ".sln", ".xaml", ".json", ".md", ".txt", ".ts", ".tsx", ".js", ".jsx", ".css", ".html", ".py", ".dart", ".yaml", ".yml", ".xml", ".sql", ".rs", ".go", ".java", ".cpp", ".h", ".toml" };
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".vs", "bin", "obj", "node_modules", ".env", "secrets.json", "appsettings.Production.json", "dist", "build" };
    public string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Aucun dossier source associé au projet.");
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) && full + Path.DirectorySeparatorChar != basePath)
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
    public string List(string relative = ".")
    {
        var directory = Resolve(relative);
        return string.Join('\n', Directory.EnumerateFileSystemEntries(directory).Where(p => !Excluded.Contains(Path.GetFileName(p)) && !Path.GetFileName(p).StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
            .Take(300).Select(p => (Directory.Exists(p) ? "[dossier] " : "") + Path.GetRelativePath(root, p)));
    }
    public async Task<string> ReadAsync(string relative, CancellationToken ct)
    {
        var full = Resolve(relative);
        if (!Extensions.Contains(Path.GetExtension(full))) throw new InvalidOperationException("Format source non pris en charge.");
        if (new FileInfo(full).Length > 128_000) throw new InvalidOperationException("Fichier trop volumineux (128 Ko maximum).");
        return await File.ReadAllTextAsync(full, ct);
    }
    public async Task<string> WriteAsync(string relative, string content, CancellationToken ct)
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

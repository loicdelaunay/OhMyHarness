using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

/// <summary>Host-owned snapshots. Containers never mount these directories or the original project.</summary>
public sealed class SandboxWorkspace : IDisposable
{
    public const long MaxBytes = 64 * 1024 * 1024;
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(PlatformSupport.PathComparer);
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".vs", ".idea", "bin", "obj", "node_modules", "dist", "build", "sandboxes", "secrets.json", "appsettings.Production.json" };
    static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".png", ".jpg", ".webp" };
    readonly SemaphoreSlim gate;
    readonly string directory;
    readonly Dictionary<string, string> originals;
    bool disposed;
    public string WorkDirectory => Path.Combine(directory, "work");
    string Baseline => Path.Combine(directory, "baseline");
    public IReadOnlyList<string> WorkRoots => originals.Keys.Select(alias => Path.Combine(WorkDirectory, alias)).ToList();
    SandboxWorkspace(string directory, Dictionary<string, string> originals, SemaphoreSlim gate)
    { this.directory = directory; this.originals = originals; this.gate = gate; }

    public const string InfoFr = "Copie privée des sources par conversation. Les commandes Linux s'exécutent dans Docker/Podman, sans réseau ni accès aux dossiers du PC. Image requise : node:22-bookworm (à télécharger au préalable). Limites : 1 CPU, 512 Mio, 128 processus, 30 s par défaut, jusqu’à 10 min par commande, 30 min par génération ; sources : 64 Mio. Les clés API et SQLite restent dans l'application. Le navigateur, le bureau, MCP et OpenCode sont désactivés dans ce mode. Les fichiers exclus (secrets connus, binaires, dépendances, .git) ne sont pas copiés. Vérifiez vos sources avant usage : un secret dans un fichier de code reste du code. Les modifications restent dans la copie jusqu'à votre validation dans « Examiner les modifications sandbox ». L'onglet outils manuel reste local. Ce mode nécessite un moteur de conteneurs Linux actif ; aucun repli local automatique.";
    public const string InfoEn = "Private source copy per conversation. Linux commands run in Docker/Podman with no network or access to PC folders. Required image: node:22-bookworm (download beforehand). Limits: 1 CPU, 512 MiB, 128 processes, 30 s default, up to 10 min per command, 30 min per generation; sources: 64 MiB. API keys and SQLite stay in the app. Browser, desktop, MCP and OpenCode are disabled in this mode. Known secrets, binaries, dependencies and .git are excluded. Review your sources: a secret embedded in code is still code. Changes stay in the copy until you approve them via Review sandbox changes. The manual tools panel remains local. Requires a running Linux container engine; never falls back to local execution.";
    public static bool Allowed(string tool) => MemoryTools.Handles(tool) || TerminalHub.Handles(tool) || tool is "list_sources" or "read_source" or "write_source" or "edit_source" or "glob_sources" or "grep_sources" or "apply_patch" or "patch_sources" or "run_terminal" or "git_changes" or "load_skill" or "read_skill_resource" or "skill_locations" or "delegate_tasks" or "todowrite" or "question";
    public static void Demand(bool enabled, string tool)
    { if (enabled && !Allowed(tool)) throw new UnauthorizedAccessException("Sandbox : outil extérieur interdit / External tool blocked: " + tool); }
    public static void Filter(JsonArray definitions, bool enabled)
    {
        if (!enabled) return;
        for (int i = definitions.Count - 1; i >= 0; i--)
        {
            var fn = definitions[i]?["function"]; var name = fn?["name"]?.GetValue<string>() ?? "";
            if (!Allowed(name)) definitions.RemoveAt(i);
            else if (name == "run_terminal") fn!["description"] = "Execute a Linux sh command inside an offline container. Sources in /workspace/<alias>; default directory is the first project. No host access. 30 second default limit, configurable up to 600 seconds. Only source files are retained between commands, no servers survive container cleanup or the end of the generation; dependencies are not retained. Git reflects the current copied sources, not the original repository history.";
        }
    }
    public static async Task<SandboxWorkspace> OpenAsync(string database, int chatId, IEnumerable<string> roots, CancellationToken ct)
    {
        var aliases = new SourceAccess(roots).Aliases.ToDictionary(x => x.Key, x => x.Value, PlatformSupport.PathComparer);
        if (aliases.Count == 0) throw new InvalidOperationException("Sandbox : associez un dossier source / Attach a source folder.");
        foreach (var (alias, path) in aliases) { ValidateName(alias); AssertNoLinks(path); }
        if (aliases.Values.Any(a => aliases.Values.Any(b => !PlatformSupport.PathComparer.Equals(a, b) && b.StartsWith(a.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison))))
            throw new InvalidOperationException("Sandbox : associez uniquement des dossiers sources sans imbrication / Source roots must not overlap.");
        var db = Path.GetFullPath(database);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(db)))[..16];
        var dir = Path.Combine(Path.GetDirectoryName(db)!, "sandboxes", id, chatId.ToString());
        AssertNoLinks(dir);
        var gate = Gates.GetOrAdd(dir, _ => new(1, 1));
        if (!await gate.WaitAsync(0, ct)) throw new InvalidOperationException("Sandbox occupée / Sandbox is busy.");
        var workspace = new SandboxWorkspace(dir, aliases, gate);
        try
        {
            var manifest = Path.Combine(dir, "roots.json");
            if (File.Exists(manifest))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(manifest, ct))!;
                if (saved.Count != aliases.Count || aliases.Any(x => !saved.TryGetValue(x.Key, out var value) || !PlatformSupport.PathComparer.Equals(value, x.Value)))
                    throw new InvalidOperationException("Les sources ont changé : créez une nouvelle conversation sandbox / Source roots changed: start a new sandbox conversation.");
            }
            else
            {
                // Snapshot first, then publish the manifest. A failed initialization never becomes reusable.
                var snapshot = new Dictionary<string, byte[]>(PlatformSupport.PathComparer);
                foreach (var (alias, root) in aliases)
                    foreach (var (name, bytes) in await ReadFilesAsync(root, ct))
                    {
                        var full = Path.GetFullPath(Path.Combine(root, name));
                        if (new[] { db, db + "-wal", db + "-shm" }.Contains(full, PlatformSupport.PathComparer)) continue;
                        snapshot.Add(alias + "/" + name, bytes);
                    }
                CheckSize(snapshot);
                await workspace.ReplaceWorkAsync(snapshot, ct);
                // Recover an interrupted first copy without retaining stale files from that attempt.
                if (Directory.Exists(workspace.Baseline)) { AssertNoLinks(workspace.Baseline); Directory.Delete(workspace.Baseline, true); }
                await WriteFilesAsync(workspace.Baseline, snapshot, ct);
                await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(aliases), ct);
            }
            foreach (var root in workspace.WorkRoots) Directory.CreateDirectory(root);
            return workspace;
        }
        catch { workspace.Dispose(); throw; }
    }

    internal static void AssertNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Sandbox : liens symboliques exclus / Symbolic links excluded.");
    }
    public static string ValidateName(string name)
    {
        var parts = name.Split('/');
        if (string.IsNullOrWhiteSpace(name) || name.Length > 500 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => char.IsControl(c) || "\\:<>\"|?*".Contains(c)) ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("Sandbox : chemin non portable ou dangereux / Unsafe path: " + name);
        return name;
    }
    static bool IncludedPath(string name) => !name.Split('/').Any(p => Excluded.Contains(p) || p.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) &&
        !Path.GetFileName(name).StartsWith("database.sqlite", StringComparison.OrdinalIgnoreCase);
    static bool Included(string name, byte[] bytes) => IncludedPath(name) &&
        (ImageExtensions.Contains(Path.GetExtension(name)) || SourceText.TryDecode(bytes, out _));
    internal static async Task<Dictionary<string, byte[]>> ReadFilesAsync(string root, CancellationToken ct)
    {
        AssertNoLinks(root);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(root);
        long total = 0; int visited = 0;
        while (pending.Count > 0)
            foreach (var path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                ct.ThrowIfCancellationRequested();
                if (++visited > 50000) throw new IOException("Sandbox : trop de fichiers / Too many files.");
                var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || !IncludedPath(name)) continue;
                ValidateName(name);
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(path); continue; }
                var length = new FileInfo(path).Length;
                if (length > 2 * 1024 * 1024) continue;
                var bytes = await File.ReadAllBytesAsync(path, ct);
                if (!Included(name, bytes)) continue;
                if ((total += length) > MaxBytes || files.Count >= 10000) throw new IOException("Sandbox : limite de copie dépassée (2 Mio/fichier, 64 Mio au total, 10 000 fichiers).");
                files.Add(name, bytes);
            }
        CheckSize(files); return files;
    }
    static void CheckSize(Dictionary<string, byte[]> files)
    { if (files.Count > 10000 || files.Values.Any(b => b.Length > 2 * 1024 * 1024) || files.Values.Sum(b => (long)b.Length) > MaxBytes) throw new IOException("Sandbox : fichiers trop volumineux / Size limit exceeded."); }
    static async Task WriteFilesAsync(string root, Dictionary<string, byte[]> files, CancellationToken ct)
    {
        foreach (var (name, bytes) in files)
        {
            var path = Path.Combine(root, ValidateName(name).Replace('/', Path.DirectorySeparatorChar));
            AssertNoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, ct);
        }
        Directory.CreateDirectory(root);
    }
    internal async Task ReplaceWorkAsync(Dictionary<string, byte[]> files, CancellationToken ct)
    {
        CheckSize(files);
        foreach (var name in files.Keys) { ValidateName(name); if (!originals.ContainsKey(name.Split('/')[0])) throw new UnauthorizedAccessException("Unknown source root."); }
        // Stage all validated regular files. No container can race these host operations (there are no bind mounts).
        var staged = Path.Combine(directory, "stage-" + Guid.NewGuid().ToString("N"));
        await WriteFilesAsync(staged, files, ct);
        ct.ThrowIfCancellationRequested();
        AssertNoLinks(WorkDirectory);
        var previous = Path.Combine(directory, "previous-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(WorkDirectory)) Directory.Move(WorkDirectory, previous);
        try { Directory.Move(staged, WorkDirectory); }
        catch { if (Directory.Exists(previous)) Directory.Move(previous, WorkDirectory); throw; }
        if (Directory.Exists(previous)) Directory.Delete(previous, true);
        foreach (var root in WorkRoots) Directory.CreateDirectory(root);
    }
    public async Task<byte[]> ExportAsync(CancellationToken ct)
    {
        using var stream = new MemoryStream();
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true))
            foreach (var (name, bytes) in await ReadFilesAsync(WorkDirectory, ct))
            {
                using var data = new MemoryStream(bytes);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data, Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead });
            }
        return stream.ToArray();
    }
    public async Task ImportAsync(Stream archive, CancellationToken ct)
    {
        using var reader = new TarReader(archive, leaveOpen: true);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int entries = 0; long total = 0;
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(cancellationToken: ct)) != null)
        {
            if (++entries > 20000) throw new IOException("Sandbox : archive trop volumineuse.");
            var name = entry.Name;
            if (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
            if (entry.EntryType == TarEntryType.Directory) { if (name is not ("" or ".")) ValidateName(name.TrimEnd('/')); continue; }
            ValidateName(name);
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new UnauthorizedAccessException("Sandbox : liens et fichiers spéciaux interdits dans l'archive.");
            if (entry.Length > 2 * 1024 * 1024 || (total += entry.Length) > MaxBytes) throw new IOException("Sandbox : archive trop volumineuse.");
            using var data = new MemoryStream();
            if (entry.DataStream != null) await entry.DataStream.CopyToAsync(data, ct);
            var bytes = data.ToArray();
            if (!Included(name, bytes)) continue;
            files.Add(name, bytes);
        }
        await ReplaceWorkAsync(files, ct);
    }
    public sealed record Review(SourceAccess Sources, SourceAccess.PatchPlan Plan, string Diff, int Count);
    public async Task<Review> ReviewAsync(CancellationToken ct)
    {
        var before = await ReadFilesAsync(Baseline, ct); var after = await ReadFilesAsync(WorkDirectory, ct);
        var source = new SourceAccess(originals.Values);
        var plan = new SourceAccess.PatchPlan(); var diff = new StringBuilder();
        foreach (var name in before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(name, out var oldBytes); after.TryGetValue(name, out var newBytes);
            if (oldBytes is not null && newBytes is not null && oldBytes.SequenceEqual(newBytes)) continue;
            var parts = name.Split('/', 2);
            var target = source.Resolve(Path.Combine(originals[parts[0]], parts[1]));
            plan.Changes.Add(new(target, oldBytes, newBytes));
            if ((oldBytes?.Contains((byte)0) ?? false) || (newBytes?.Contains((byte)0) ?? false))
                diff.AppendLine($"--- {(oldBytes == null ? "/dev/null" : "a/" + name)}\n+++ {(newBytes == null ? "/dev/null" : "b/" + name)}\nBinary: {oldBytes?.Length ?? 0} -> {newBytes?.Length ?? 0} bytes\nSHA256: {(oldBytes == null ? "none" : Convert.ToHexString(SHA256.HashData(oldBytes)))} -> {(newBytes == null ? "none" : Convert.ToHexString(SHA256.HashData(newBytes)))}");
            else
            {
                var textDiff = SourceAccess.SandboxDiff(name, oldBytes == null, Encoding.UTF8.GetString(oldBytes ?? []), Encoding.UTF8.GetString(newBytes ?? []));
                if (newBytes == null) textDiff = textDiff.Replace("+++ b/" + name + "\n", "+++ /dev/null\n", StringComparison.Ordinal);
                diff.Append(textDiff);
            }
            if (diff.Length > 500000 || plan.Changes.Count > 200) throw new IOException("Sandbox : diff trop volumineux pour validation (200 fichiers / 500 000 caractères).");
        }
        plan.Diff = diff.Length == 0 ? "Aucune modification / No changes." : diff.ToString();
        return new(source, plan, plan.Diff, plan.Changes.Count);
    }
    public async Task<string> ApplyAsync(Review review, CancellationToken ct)
    {
        foreach (var path in originals.Values) AssertNoLinks(path);
        var result = await review.Sources.ApplyPatchAsync(review.Plan, ct);
        // Advance only the applied baseline. A later run keeps the reviewed copy.
        foreach (var change in review.Plan.Changes)
        {
            var pair = originals.First(x => change.Path.StartsWith(x.Value.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison));
            var path = Path.Combine(Baseline, pair.Key, Path.GetRelativePath(pair.Value, change.Path));
            AssertNoLinks(path);
            if (change.After == null) File.Delete(path);
            else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, change.After, CancellationToken.None); }
        }
        return result;
    }
    public void Dispose() { if (!disposed) { disposed = true; gate.Release(); } }
}

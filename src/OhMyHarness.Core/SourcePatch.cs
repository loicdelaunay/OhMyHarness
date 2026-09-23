using System.Text;

namespace OhMyHarness.Core;

public sealed record SourceEdit(string Path, string? OldText, string NewText);

public sealed partial class SourceAccess
{
    public sealed class PatchPlan
    {
        internal sealed record Change(string Path, byte[]? Before, byte[]? After);
        internal List<Change> Changes { get; } = [];
        public string Diff { get; internal set; } = "";
    }

    // Null old_text creates a new file; existing files require one unambiguous exact match.
    public async Task<PatchPlan> PreparePatchAsync(IReadOnlyList<SourceEdit> edits, CancellationToken ct)
    {
        if (edits.Count is < 1 or > 50) throw new ArgumentException("Le patch doit contenir de 1 à 50 modifications.");
        if (edits.Sum(e => (long)(e.OldText?.Length ?? 0) + e.NewText.Length) > 1000000) throw new ArgumentException("Patch trop volumineux.");
        var plan = new PatchPlan(); var diff = new StringBuilder();
        var originals = new Dictionary<string, byte[]?>(PlatformSupport.PathComparer);
        var contents = new Dictionary<string, string>(PlatformSupport.PathComparer);
        var utf8 = new UTF8Encoding(false, true);
        foreach (var edit in edits)
        {
            ct.ThrowIfCancellationRequested();
            var path = Resolve(edit.Path);
            if (Directory.Exists(path)) throw new InvalidOperationException("Le chemin cible est un dossier.");
            if (!originals.ContainsKey(path))
            {
                if (File.Exists(path) && new FileInfo(path).Length > 128000) throw new InvalidOperationException("Fichier >128 Ko.");
                var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
                if (bytes is not null && (!SourceText.TryDecode(bytes, out var decoded) || decoded.Encoding is not UTF8Encoding))
                    throw new InvalidOperationException($"Patch refusé dans {edit.Path} : fichier binaire ou encodage non UTF-8.");
                originals[path] = bytes;
                contents[path] = bytes is null ? "" : utf8.GetString(bytes.AsSpan(bytes.AsSpan().StartsWith(new byte[] {239,187,191}) ? 3 : 0));
            }
            var current = contents[path];
            if (edit.OldText is null)
            {
                if (originals[path] is not null || plan.Changes.Any(x => PlatformSupport.PathComparer.Equals(x.Path, path))) throw new InvalidOperationException("Création refusée : fichier déjà présent.");
                contents[path] = edit.NewText;
                // Reserve the path so a second create in this batch is rejected.
                plan.Changes.Add(new(path, null, []));
            }
            else
            {
                if (edit.OldText.Length == 0) throw new ArgumentException("old_text ne peut pas être vide.");
                var index = current.IndexOf(edit.OldText, StringComparison.Ordinal);
                if (index < 0 || current.IndexOf(edit.OldText, index + 1, StringComparison.Ordinal) >= 0)
                    throw new InvalidOperationException($"Patch refusé dans {edit.Path} : texte absent ou ambigu ; fournissez davantage de contexte.");
                contents[path] = current.Remove(index, edit.OldText.Length).Insert(index, edit.NewText);
            }
            if (utf8.GetByteCount(contents[path]) > 128000) throw new InvalidOperationException("Résultat >128 Ko.");
        }
        plan.Changes.Clear();
        foreach (var (path, before) in originals)
        {
            var after = utf8.GetBytes(contents[path]);
            if (before is not null && before.AsSpan().StartsWith(new byte[] {239,187,191})) after = new byte[] {239,187,191}.Concat(after).ToArray();
            if (before is not null && before.SequenceEqual(after)) continue;
            plan.Changes.Add(new(path, before, after));
            var root = _roots.First(r => path.StartsWith(r.FullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison));
            var display = (_roots.Count > 1 ? root.Alias + "/" : "") + Path.GetRelativePath(root.FullPath, path).Replace('\\', '/');
            var oldContent = before is null ? "" : utf8.GetString(before).TrimStart('\uFEFF');
            diff.Append(UnifiedDiff(display, before is null, oldContent, contents[path]));
        }
        plan.Diff = diff.Length == 0 ? "Aucune modification." : diff.ToString();
        if (plan.Diff.Length > 200000) throw new InvalidOperationException("Diff trop volumineux ; divisez le patch.");
        return plan;
    }

    static string UnifiedDiff(string path, bool created, string before, string after)
    {
        static string[] Lines(string text)
        {
            text = text.Replace("\r\n", "\n");
            return text.Length == 0 ? [] : (text.EndsWith('\n') ? text[..^1] : text).Split('\n');
        }
        var oldLines = Lines(before); var newLines = Lines(after);
        int prefix = 0, suffix = 0;
        while (prefix < Math.Min(oldLines.Length, newLines.Length) && oldLines[prefix] == newLines[prefix]
            && !(prefix == oldLines.Length - 1 && !before.EndsWith('\n'))
            && !(prefix == newLines.Length - 1 && !after.EndsWith('\n'))) prefix++;
        // Keep final-line newline changes inside the edited range.
        if (before.EndsWith('\n') != after.EndsWith('\n')) prefix = Math.Min(prefix, Math.Max(0, Math.Min(oldLines.Length, newLines.Length) - 1));
        if (before.EndsWith('\n') == after.EndsWith('\n'))
            while (suffix < Math.Min(oldLines.Length, newLines.Length) - prefix && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;
        int start = Math.Max(0, prefix - 3), contextEnd = Math.Min(suffix, 3);
        int oldEnd = oldLines.Length - suffix, newEnd = newLines.Length - suffix;
        var output = new StringBuilder($"--- {(created ? "/dev/null" : "a/" + path)}\n+++ b/{path}\n@@ -{(oldLines.Length == 0 ? 0 : start + 1)},{oldEnd + contextEnd - start} +{(newLines.Length == 0 ? 0 : start + 1)},{newEnd + contextEnd - start} @@\n");
        void Line(char kind, string[] lines, int index, string content)
        {
            output.AppendLine(kind + lines[index]);
            if (index == lines.Length - 1 && !content.EndsWith('\n')) output.AppendLine("\\ No newline at end of file");
        }
        for (int i = start; i < prefix; i++) Line(' ', oldLines, i, before);
        for (int i = prefix; i < oldEnd; i++) Line('-', oldLines, i, before);
        for (int i = prefix; i < newEnd; i++) Line('+', newLines, i, after);
        for (int i = 0; i < contextEnd; i++) Line(' ', newLines, newEnd + i, after);
        return output.ToString();
    }

    internal static string SandboxDiff(string path, bool created, string before, string after) => UnifiedDiff(path, created, before, after);

    public async Task<string> ApplyPatchAsync(PatchPlan plan, CancellationToken ct)
    {
        await WriteGate.WaitAsync(ct);
        try { return await ApplyPreparedPatchAsync(plan, ct); }
        finally { WriteGate.Release(); }
    }

    async Task<string> ApplyPreparedPatchAsync(PatchPlan plan, CancellationToken ct)
    {
        foreach (var change in plan.Changes)
        {
            var path = Resolve(change.Path);
            if (Directory.Exists(path)) throw new InvalidOperationException("Un dossier occupe désormais le chemin cible.");
            var current = File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : null;
            if ((current is null) != (change.Before is null) || current is not null && !current.SequenceEqual(change.Before!))
                throw new InvalidOperationException("Un fichier a changé depuis la préparation du diff. Recréez le patch.");
        }
        ct.ThrowIfCancellationRequested();
        var written = new List<PatchPlan.Change>();
        try
        {
            foreach (var change in plan.Changes)
            {
                Resolve(change.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(change.Path)!);
                written.Add(change);
                if (change.After is null) File.Delete(change.Path);
                else await ReplaceFileAsync(change.Path, change.After);
            }
        }
        catch (Exception error)
        {
            var rollbackErrors = new List<Exception> { error };
            foreach (var change in written.AsEnumerable().Reverse())
                try { Resolve(change.Path); if (change.Before is null) File.Delete(change.Path); else await ReplaceFileAsync(change.Path, change.Before); }
                catch (Exception rollback) { rollbackErrors.Add(rollback); }
            if (rollbackErrors.Count > 1) throw new AggregateException("Échec du patch et restauration incomplète.", rollbackErrors);
            throw;
        }
        return $"Patch appliqué : {plan.Changes.Count} fichier(s).\n```diff\n{plan.Diff}\n```";
    }

    static async Task ReplaceFileAsync(string path, byte[] bytes)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".omh-patch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, CancellationToken.None);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

using System.Text.Json;

namespace OhMyHarness.Core;

/// <summary>Portable drawings scoped to a database and conversation, with atomic revision-checked writes.</summary>
public sealed class AssetWorkspace
{
    public string Root { get; }
    public AssetWorkspace(string database, int chatId)
    {
        if (chatId <= 0) throw new ArgumentException("A saved conversation is required.");
        Root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database))!, "assets", "chat-" + chatId);
    }
    public static void ValidId(string id)
    {
        if (id.Length is < 1 or > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" }.Contains(id.ToUpperInvariant())) throw new ArgumentException("Use 1–64 letters, digits, '-' or '_' for IDs.");
    }
    string FilePath(string id) { ValidId(id); var path = Path.Combine(Root, id + ".json"); SandboxWorkspace.AssertNoLinks(path); return path; }
    public async Task<List<AssetDocument>> ListAsync(CancellationToken ct)
    {
        SandboxWorkspace.AssertNoLinks(Root);
        if (!Directory.Exists(Root)) return [];
        var result = new List<AssetDocument>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(100))
        {
            ct.ThrowIfCancellationRequested();
            try { result.Add(await ReadAsync(Path.GetFileNameWithoutExtension(file), ct)); }
            catch (Exception ex) when (ex is JsonException or ArgumentException or IOException) { /* One damaged document must not hide the others. */ }
        }
        return result;
    }
    public async Task<AssetDocument> ReadAsync(string id, CancellationToken ct)
    {
        var path = FilePath(id);
        if (new FileInfo(path).Length > 2_000_000) throw new IOException("Asset document exceeds 2 MB.");
        var doc = JsonSerializer.Deserialize<AssetDocument>(await File.ReadAllTextAsync(path, ct), AssetDocument.Json) ?? throw new IOException("Invalid asset document.");
        if (doc.Id != id) throw new IOException("Asset ID does not match its file.");
        doc.Validate(); return doc;
    }
    async Task<FileStream> LockAsync(CancellationToken ct)
    {
        SandboxWorkspace.AssertNoLinks(Root); Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, ".write.lock"); SandboxWorkspace.AssertNoLinks(path);
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 50) { await Task.Delay(100, ct); }
        }
    }
    static async Task WriteAtomic(string path, byte[] bytes, bool overwrite, CancellationToken ct)
    {
        SandboxWorkspace.AssertNoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".write-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(temp, bytes, ct); ct.ThrowIfCancellationRequested(); File.Move(temp, path, overwrite); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public Task<AssetDocument> CreateAsync(string name, int width, int height, string background, CancellationToken ct)
        => CreateAsync(name, width, height, background, 1, ct);
    public async Task<AssetDocument> CreateAsync(string name, int width, int height, string background, int pixelSize, CancellationToken ct)
    {
        var doc = new AssetDocument { Name = name, Width = width, Height = height, Background = background, PixelSize = pixelSize, Revision = 1 }; doc.Validate();
        using var guard = await LockAsync(ct);
        if (Directory.EnumerateFiles(Root, "*.json").Take(100).Count() >= 100) throw new IOException("Maximum 100 assets per conversation.");
        await WriteAtomic(FilePath(doc.Id), JsonSerializer.SerializeToUtf8Bytes(doc, AssetDocument.Json), false, ct); return doc;
    }
    public async Task<AssetDocument> UpdateAsync(string id, int expectedRevision, Action<AssetDocument> change, CancellationToken ct)
    {
        using var guard = await LockAsync(ct);
        var doc = await ReadAsync(id, ct);
        if (doc.Revision != expectedRevision) throw new InvalidOperationException("Asset changed. Read it again and use its current revision.");
        change(doc); doc.Validate(); doc.Revision++;
        await WriteAtomic(FilePath(id), JsonSerializer.SerializeToUtf8Bytes(doc, AssetDocument.Json), true, ct); return doc;
    }
    public async Task<string> ExportAsync(AssetDocument doc, string format, bool transparent, string? background, float scale, CancellationToken ct)
    {
        ValidId(doc.Id);
        var bytes = await Task.Run(() => AssetRenderer.Export(doc, format, transparent, background, scale), ct);
        var extension = format switch { "svg-animated" => "svg", "frames" => "zip", _ => format };
        var path = Path.Combine(Root, "exports", $"{doc.Id}-r{doc.Revision}-{Guid.NewGuid().ToString("N")[..8]}.{extension}");
        await WriteAtomic(path, bytes, false, ct); return path;
    }
}

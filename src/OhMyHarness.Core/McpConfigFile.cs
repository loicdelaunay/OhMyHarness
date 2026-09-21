using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

/// <summary>Standard portable MCP configuration, mirrored into SQLite for existing tools.</summary>
public static class McpConfigFile
{
    public static string FilePath => Path.Combine(PortableStorage.Root, "MCP.json");
    static readonly SemaphoreSlim gate = new(1, 1);
    static readonly Dictionary<string, string> imported = new(StringComparer.Ordinal);
    public sealed record Entry(McpServer Server, string? Secrets);
    public const string Empty = "{\n  \"mcpServers\": {}\n}";

    public static IReadOnlyList<Entry> Parse(string text)
    {
        if (text.Length > 1_000_000) throw new ArgumentException("MCP.json : 1 Mo maximum / 1 MB maximum.");
        var root = JsonNode.Parse(text) as JsonObject ?? throw new ArgumentException("MCP.json : objet JSON requis.");
        var servers = root["mcpServers"] as JsonObject ?? throw new ArgumentException("MCP.json : objet mcpServers requis.");
        var result = new List<Entry>();
        foreach (var (name, node) in servers)
        {
            var item = node as JsonObject ?? throw new ArgumentException($"MCP {name} : objet requis.");
            string S(string key, string fallback = "") => item[key]?.GetValue<string>() ?? fallback;
            var args = item["args"]?.Deserialize<string[]>() ?? [];
            if (args.Any(x => x == null)) throw new ArgumentException($"MCP {name} : args doit contenir des chaînes.");
            var server = new McpServer { Name = name, Enabled = item["enabled"]?.GetValue<bool>() ?? true,
                Transport = S("transport", item.ContainsKey("url") ? "http" : "stdio"), Command = S("command"),
                ArgumentsJson = JsonSerializer.Serialize(args), WorkingDirectory = S("cwd", S("workingDirectory")), Url = S("url") };
            server.Validate();
            string? secrets = null;
            if (item.ContainsKey("env") || item.ContainsKey("headers"))
            {
                Dictionary<string, string> Values(string key)
                {
                    if (!item.ContainsKey(key)) return [];
                    var values = item[key] as JsonObject ?? throw new ArgumentException($"MCP {name} : {key} doit être un objet.");
                    return values.ToDictionary(x => x.Key, x => x.Value?.GetValue<string>() ?? throw new ArgumentException($"MCP {name} : {key} doit contenir des chaînes."));
                }
                secrets = JsonSerializer.Serialize(new McpSecrets { Environment = Values("env"), Headers = Values("headers") });
            }
            result.Add(new(server, secrets));
        }
        return result;
    }

    public static string Export(IEnumerable<McpServer> servers, string? previousJson = null, ISet<string>? changedSecrets = null)
    {
        var items = new JsonObject();
        var previous = previousJson == null ? null : JsonNode.Parse(previousJson)?["mcpServers"] as JsonObject;
        foreach (var server in servers)
        {
            if (items.ContainsKey(server.Name)) throw new ArgumentException("Noms MCP uniques requis / MCP names must be unique.");
            var item = new JsonObject { ["enabled"] = server.Enabled, ["transport"] = server.Transport };
            if (server.Transport == "stdio")
            {
                item["command"] = server.Command; item["args"] = JsonNode.Parse(server.ArgumentsJson);
                if (server.WorkingDirectory.Length > 0) item["cwd"] = server.WorkingDirectory;
            }
            else item["url"] = server.Url;
            // Never decrypt SQLite secrets for export. Only retain values already supplied in the file.
            if (changedSecrets?.Contains(server.Name) != true && previous?[server.Name] is JsonObject prior)
            {
                foreach (var key in new[] { "env", "headers" })
                    if (prior.ContainsKey(key)) item[key] = prior[key]?.DeepClone();
            }
            items[server.Name] = item;
        }
        return new JsonObject { ["mcpServers"] = items }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    static async Task ApplyAsync(HarnessDb db, IReadOnlyList<Entry> entries, Func<string, CancellationToken, Task<byte[]>> encrypt, CancellationToken ct,
        Func<byte[], CancellationToken, Task<string>>? decrypt = null)
    {
        var existing = await db.McpServers.ToListAsync(ct);
        // Prepare encryption before modifying any tracked entities.
        var secrets = new Dictionary<string, byte[]>();
        foreach (var entry in entries)
        {
            if (entry.Secrets == null) continue;
            var old = existing.FirstOrDefault(x => x.Name == entry.Server.Name);
            // Compare internally only. Re-encrypting unchanged values would change permission fingerprints.
            // Decrypted SQLite values are never passed to Export or written to MCP.json.
            if (decrypt != null && old?.ProtectedSecrets.Length > 0)
            {
                try
                {
                    var before = McpSecrets.Parse(await decrypt(old.ProtectedSecrets, ct));
                    var after = McpSecrets.Parse(entry.Secrets);
                    bool Same(Dictionary<string,string> a, Dictionary<string,string> b) => a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var value) && value == x.Value);
                    if (Same(before.Environment, after.Environment) && Same(before.Headers, after.Headers)) continue;
                }
                catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidOperationException) { }
            }
            secrets[entry.Server.Name] = await encrypt(entry.Secrets, ct);
        }
        foreach (var old in existing.Where(old => !entries.Any(e => e.Server.Name == old.Name))) db.McpServers.Remove(old);
        foreach (var entry in entries)
        {
            var s = entry.Server;
            var target = existing.FirstOrDefault(x => x.Name == s.Name);
            if (target == null) { target = new McpServer(); db.McpServers.Add(target); }
            target.Name = s.Name; target.Enabled = s.Enabled; target.Transport = s.Transport; target.Command = s.Command;
            target.ArgumentsJson = s.ArgumentsJson; target.WorkingDirectory = s.WorkingDirectory; target.Url = s.Url;
            if (secrets.TryGetValue(s.Name, out var value)) target.ProtectedSecrets = value;
        }
        await db.SaveChangesAsync(ct);
    }

    static async Task WriteAsync(string path, string text, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, text, ct); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        imported[path] = text;
    }

    public static async Task SyncAsync(HarnessDb db, Func<string, CancellationToken, Task<byte[]>> encrypt,
        CancellationToken ct = default, string? path = null, Func<byte[], CancellationToken, Task<string>>? decrypt = null)
    {
        path ??= FilePath;
        await gate.WaitAsync(ct);
        try
        {
            if (!File.Exists(path)) { await WriteAsync(path, Export(await db.McpServers.AsNoTracking().ToListAsync(ct)), ct); return; }
            var text = await File.ReadAllTextAsync(path, ct);
            if (imported.GetValueOrDefault(path) == text) return;
            await ApplyAsync(db, Parse(text), encrypt, ct, decrypt); imported[path] = text;
        }
        finally { gate.Release(); }
    }

    public static async Task SaveJsonAsync(HarnessDb db, string text, string expected,
        Func<string, CancellationToken, Task<byte[]>> encrypt, CancellationToken ct = default, string? path = null, Func<byte[], CancellationToken, Task<string>>? decrypt = null)
    {
        var entries = Parse(text); path ??= FilePath;
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(path) && await File.ReadAllTextAsync(path, ct) != expected)
                throw new IOException("MCP.json a été modifié. Rechargez le fichier avant d’enregistrer. / Reload the changed file before saving.");
            // The validated file is authoritative; a failed SQLite sync can be retried on next load.
            await WriteAsync(path, text, ct); imported.Remove(path);
            await ApplyAsync(db, entries, encrypt, ct, decrypt); imported[path] = text;
        }
        finally { gate.Release(); }
    }

    public static async Task PublishAsync(HarnessDb db, CancellationToken ct = default, ISet<string>? changedSecrets = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            var path = FilePath;
            if (File.Exists(path) && imported.TryGetValue(path, out var expected) && await File.ReadAllTextAsync(path, ct) != expected)
                throw new IOException("MCP.json a changé sur disque : rechargez sa configuration.");
            var previous = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
            await WriteAsync(path, Export(await db.McpServers.AsNoTracking().ToListAsync(ct), previous, changedSecrets), ct);
        }
        finally { gate.Release(); }
    }
}

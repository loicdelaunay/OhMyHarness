using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OhMyHarness.Core;

public sealed class McpServer
{
    public int Id { get; set; }
    public string Name { get; set; } = "MCP";
    public bool Enabled { get; set; }
    public string Transport { get; set; } = "stdio";
    public string Command { get; set; } = "";
    public string ArgumentsJson { get; set; } = "[]";
    public string WorkingDirectory { get; set; } = "";
    public string Url { get; set; } = "";
    public byte[] ProtectedSecrets { get; set; } = [];
    public override string ToString() => Name;

    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { Id, Transport, Command, ArgumentsJson, WorkingDirectory, Url, ProtectedSecrets }))));
    public string ConnectionDetails => Transport == "stdio" ? $"{Command}\n{ArgumentsJson}\n{WorkingDirectory}" : Url;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("Nom MCP requis / MCP name required.");
        if (Transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(Command)) throw new ArgumentException("Commande MCP requise / MCP command required.");
            _ = JsonSerializer.Deserialize<string[]>(ArgumentsJson) ?? throw new ArgumentException("Arguments: JSON array required.");
            if (!string.IsNullOrWhiteSpace(WorkingDirectory) && (!Path.IsPathFullyQualified(WorkingDirectory) || !Directory.Exists(WorkingDirectory)))
                throw new ArgumentException("MCP working directory must be an existing absolute path.");
        }
        else if (Transport is "http" or "sse")
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback))
                throw new ArgumentException("MCP: HTTPS required (HTTP allowed on localhost).");
        }
        else throw new ArgumentException("MCP transport: stdio, http or sse.");
    }
}

public sealed class McpSecrets
{
    public Dictionary<string, string> Environment { get; set; } = [];
    public Dictionary<string, string> Headers { get; set; } = [];
    public static McpSecrets Parse(string json) => string.IsNullOrWhiteSpace(json) ? new() :
        JsonSerializer.Deserialize<McpSecrets>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new ArgumentException("MCP secrets: JSON object required.");
}

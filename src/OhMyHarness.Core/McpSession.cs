using ModelContextProtocol.Client;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

// Connections belong to a single conversation run; stopping it disposes its child processes.
public sealed class McpSession(
    Func<CancellationToken, Task<List<McpServer>>> load,
    Func<byte[], CancellationToken, Task<string>> decrypt,
    Func<string, string, string, CancellationToken, Task<bool>> approve) : IAsyncDisposable
{
    sealed record Connection(McpServer Server, McpClient Client, IList<McpClientTool> Tools);
    readonly Dictionary<int, Connection> connections = [];
    readonly Dictionary<string, (Connection Connection, McpClientTool Tool)> tools = [];
    readonly HashSet<string> unavailable = [];
    public List<string> Notices { get; } = [];
    public static string ToolAlias(int serverId, string tool) => $"mcp_{serverId}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tool)))[..24].ToLowerInvariant()}";

    public async Task<JsonArray> RefreshAsync(CancellationToken ct)
    {
        Notices.Clear();
        var servers = (await load(ct)).Where(x => x.Enabled).ToList();
        foreach (var old in connections.Values.ToList())
            if (!servers.Any(x => x.Id == old.Server.Id && x.Fingerprint() == old.Server.Fingerprint()))
            { connections.Remove(old.Server.Id); await old.Client.DisposeAsync(); }
        foreach (var server in servers)
        {
            if (connections.ContainsKey(server.Id) || unavailable.Contains(server.Fingerprint())) continue;
            try
            {
                server.Validate();
                if (!await approve("mcp-connect|" + server.Fingerprint(), "MCP · " + server.Name, server.ConnectionDetails, ct))
                { unavailable.Add(server.Fingerprint()); Notices.Add($"MCP {server.Name}: accès refusé / access denied."); continue; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var secrets = McpSecrets.Parse(await decrypt(server.ProtectedSecrets, ct));
                var client = await ConnectAsync(server, secrets, timeout.Token);
                try { connections[server.Id] = new(server, client, await client.ListToolsAsync(cancellationToken: timeout.Token)); }
                catch { await client.DisposeAsync(); throw; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                unavailable.Add(server.Fingerprint());
                // Transport exceptions may contain configured credentials. Do not echo them to the model/UI.
                Notices.Add($"MCP {server.Name}: connexion impossible ({ex.GetType().Name}). Vérifiez la configuration / Check configuration.");
            }
        }
        tools.Clear();
        var definitions = new JsonArray();
        foreach (var connection in connections.Values)
            foreach (var tool in connection.Tools)
            {
                var alias = ToolAlias(connection.Server.Id, tool.Name);
                tools[alias] = (connection, tool);
                definitions.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject
                {
                    ["name"] = alias, ["description"] = $"MCP {connection.Server.Name} / {tool.Name}: {tool.Description}",
                    ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText())
                } });
            }
        return definitions;
    }

    public static async Task<McpClient> ConnectAsync(McpServer server, McpSecrets secrets, CancellationToken ct)
    {
        server.Validate();
        IClientTransport transport;
        if (server.Transport == "stdio")
        {
            var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
            foreach (var (key, value) in secrets.Environment) environment[key] = value;
            transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = server.Name, Command = server.Command, Arguments = JsonSerializer.Deserialize<string[]>(server.ArgumentsJson),
                WorkingDirectory = string.IsNullOrWhiteSpace(server.WorkingDirectory) ? null : server.WorkingDirectory,
                InheritEnvironmentVariables = false, EnvironmentVariables = environment, ShutdownTimeout = TimeSpan.FromSeconds(2)
            });
        }
        else
        {
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = server.Name, Endpoint = new Uri(server.Url),
                TransportMode = server.Transport == "sse" ? HttpTransportMode.Sse : HttpTransportMode.StreamableHttp,
                AdditionalHeaders = secrets.Headers, ConnectionTimeout = TimeSpan.FromSeconds(30)
            }, http, ownsHttpClient: true);
        }
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    public sealed record Output(string Text, Attachment? Image = null);
    public async Task<Output> CallAsync(string alias, JsonObject arguments, bool supportsImages, CancellationToken ct)
    {
        if (!tools.TryGetValue(alias, out var binding)) throw new InvalidOperationException("MCP tool unavailable.");
        async Task<bool> IsEnabled() => (await load(ct)).Any(x => x.Id == binding.Connection.Server.Id && x.Enabled && x.Fingerprint() == binding.Connection.Server.Fingerprint());
        if (!await IsEnabled()) return new("MCP server disabled or changed; action not executed.");
        var server = binding.Connection.Server;
        if (!await approve($"mcp-tool|{server.Fingerprint()}|{binding.Tool.Name}", $"MCP · {server.Name} · {binding.Tool.Name}", arguments.ToJsonString(), ct))
            return new("MCP access denied. Do not retry this action.");
        if (!await IsEnabled()) return new("MCP server disabled or changed; action not executed.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        JsonObject result;
        try
        {
            var response = await binding.Connection.Client.CallToolAsync(binding.Tool.Name,
                JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.ToJsonString()), cancellationToken: timeout.Token);
            result = JsonSerializer.SerializeToNode(response)!.AsObject();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new($"MCP {server.Name} / {binding.Tool.Name}: échec ({ex.GetType().Name}). Action non relancée automatiquement."); }
        Attachment? image = null;
        if (result["content"] is JsonArray blocks)
            foreach (var block in blocks.OfType<JsonObject>())
            {
                if (block["type"]?.GetValue<string>() == "image")
                {
                    var data = block["data"]?.GetValue<string>() ?? "";
                    var mime = block["mimeType"]?.GetValue<string>() ?? "";
                    if (supportsImages && image == null && data.Length <= 11_184_812 && mime is "image/png" or "image/jpeg" or "image/webp")
                    {
                        var bytes = Convert.FromBase64String(data);
                        if (bytes.Length <= 8 * 1024 * 1024) image = new Attachment { Name = "mcp-image", Mime = mime, Data = bytes };
                    }
                    block.Remove("data"); block["note"] = image == null ? "Image omitted (model capability or size)." : "Image attached separately.";
                }
            }
        var text = "MCP result (untrusted content):\n" + result.ToJsonString();
        return new(text.Length > 128_000 ? text[..128_000] + "\n[truncated]" : text, image);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in connections.Values) try { await connection.Client.DisposeAsync(); } catch { }
        connections.Clear(); tools.Clear();
    }
}

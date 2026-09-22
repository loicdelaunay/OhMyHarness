using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core.Hosting;

public sealed partial class HarnessService
{
    async Task<string?> SyncMcpFile(CancellationToken ct)
    {
        try { await using var db = Db(); await McpConfigFile.SyncAsync(db, Encrypt, ct, decrypt: Decrypt); return null; }
        catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or IOException or InvalidOperationException) { return "MCP.json : " + ex.Message; }
    }
    static object McpView(McpServer s) => new { s.Id, s.Name, s.Enabled, s.Transport, s.Command, s.ArgumentsJson, s.WorkingDirectory, s.Url, hasSecrets = s.ProtectedSecrets.Length > 0 };
    McpSession CreateMcpSession(int chatId) => new(async ct =>
    {
        await using var context = Db();
        await SyncMcpFile(ct);
        var list = await context.McpServers.AsNoTracking().ToListAsync(ct);
        var features = FeatureSettings.Read((await context.States.AsNoTracking().SingleAsync(ct)).FeaturesJson);
        if(features.BrowserMode == "chrome") list.Add(features.ChromeServer(chatId));
        return list;
    }, Decrypt, Approve);

    async Task<object?> DispatchMcp(string method, JsonObject p, CancellationToken ct)
    {
        await using var context = Db();
        if (method == "mcp.json.get") { await SyncMcpFile(ct); return new { path = McpConfigFile.FilePath, content = File.Exists(McpConfigFile.FilePath) ? await File.ReadAllTextAsync(McpConfigFile.FilePath, ct) : McpConfigFile.Empty }; }
        if (method == "mcp.json.save") { await McpConfigFile.SaveJsonAsync(context, S(p,"content"), S(p,"expected"), Encrypt, ct, decrypt: Decrypt); return true; }
        var configError = await SyncMcpFile(ct);
        if (configError != null && method != "mcp.test") throw new IOException(configError);
        var id = I(p, "id");
        var server = id == 0 ? new McpServer() : await context.McpServers.SingleAsync(x => x.Id == id, ct);
        if (method == "mcp.delete") { context.McpServers.Remove(server); await context.SaveChangesAsync(ct); await McpConfigFile.PublishAsync(context, ct); return true; }
        if (method == "mcp.toggle") { server.Enabled = B(p, "enabled"); await context.SaveChangesAsync(ct); await McpConfigFile.PublishAsync(context, ct); return true; }
        if (method == "mcp.test")
        {
            server.Validate();
            if (!await Approve("mcp-connect|" + server.Fingerprint(), "MCP · " + server.Name, server.ConnectionDetails, ct)) return new { error = "Access denied." };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await using var client = await McpSession.ConnectAsync(server, McpSecrets.Parse(await Decrypt(server.ProtectedSecrets, ct)), timeout.Token);
                return new { tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).Select(x => x.Name).ToList() };
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { return new { error = "MCP connection failed: " + ex.GetType().Name }; }
        }
        server.Name = S(p, "name").Trim(); server.Enabled = B(p, "enabled"); server.Transport = S(p, "transport", "stdio");
        server.Command = S(p, "command").Trim(); server.ArgumentsJson = S(p, "argumentsJson", "[]");
        server.WorkingDirectory = S(p, "workingDirectory").Trim(); server.Url = S(p, "url").Trim(); server.Validate();
        if (await context.McpServers.AnyAsync(x => x.Id != id && x.Name == server.Name, ct)) throw new ArgumentException("Nom MCP déjà utilisé / MCP name already used.");
        if (B(p, "clearSecrets")) server.ProtectedSecrets = [];
        if (!string.IsNullOrWhiteSpace(S(p, "secrets")))
        { _ = McpSecrets.Parse(S(p, "secrets")); server.ProtectedSecrets = await Encrypt(S(p, "secrets"), ct); }
        if (id == 0) context.McpServers.Add(server);
        await context.SaveChangesAsync(ct); await McpConfigFile.PublishAsync(context, ct, B(p,"clearSecrets") || !string.IsNullOrWhiteSpace(S(p,"secrets")) ? new HashSet<string>{server.Name} : null); return McpView(server);
    }
}

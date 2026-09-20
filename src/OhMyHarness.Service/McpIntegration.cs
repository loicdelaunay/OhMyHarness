using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.Service;

public sealed partial class HarnessService
{
    static object McpView(McpServer s) => new { s.Id, s.Name, s.Enabled, s.Transport, s.Command, s.ArgumentsJson, s.WorkingDirectory, s.Url, hasSecrets = s.ProtectedSecrets.Length > 0 };
    McpSession CreateMcpSession() => new(async ct =>
    {
        await using var context = Db();
        return await context.McpServers.AsNoTracking().ToListAsync(ct);
    }, Decrypt, Approve);

    async Task<object?> DispatchMcp(string method, JsonObject p, CancellationToken ct)
    {
        await using var context = Db();
        var id = I(p, "id");
        var server = id == 0 ? new McpServer() : await context.McpServers.SingleAsync(x => x.Id == id, ct);
        if (method == "mcp.delete") { context.McpServers.Remove(server); await context.SaveChangesAsync(ct); return true; }
        if (method == "mcp.toggle") { server.Enabled = B(p, "enabled"); await context.SaveChangesAsync(ct); return true; }
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
        if (B(p, "clearSecrets")) server.ProtectedSecrets = [];
        if (!string.IsNullOrWhiteSpace(S(p, "secrets")))
        { _ = McpSecrets.Parse(S(p, "secrets")); server.ProtectedSecrets = await Encrypt(S(p, "secrets"), ct); }
        if (id == 0) context.McpServers.Add(server);
        await context.SaveChangesAsync(ct); return McpView(server);
    }
}

using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.Service;

public sealed partial class HarnessService
{
    readonly TerminalHub terminals = new();
    async Task<object?> DispatchTerminal(string method, JsonObject p, CancellationToken ct)
    {
        int id = I(p, "chatId");
        await using var db = Db();
        var chat = await db.Chats.SingleAsync(x => x.Id == id, ct);
        var project = await db.Projects.SingleAsync(x => x.Id == chat.ProjectId, ct);
        string terminal = S(p, "terminalId");
        switch (method)
        {
            case "terminals.list": return terminals.List(id);
            case "terminals.create": return terminals.Create(id, false, S(p, "name"), Root(project));
            case "terminals.delete": await terminals.DeleteAsync(id, null, terminal); return true;
            case "terminals.stop": terminals.Stop(id, null, terminal); return true;
            case "terminals.start":
                var selected = terminals.Read(id, null, terminal);
                if (selected.Sandbox) throw new InvalidOperationException("Terminal sandbox : lancement réservé à l'agent sandbox / Start this terminal from the sandbox agent.");
                if (!project.GetSourceFolders().Any(root => PlatformSupport.PathComparer.Equals(Path.GetFullPath(root), selected.Directory))) throw new UnauthorizedAccessException("Dossier détaché du projet.");
                return terminals.Start(id, false, terminal, S(p, "command"), (command, output, token) => WorkspaceTools.ShellAsync(command, selected.Directory, token, output), CancellationToken.None);
            default: throw new ArgumentException("Unknown terminal method.");
        }
    }
}

using OhMyHarness.Core;
using OhMyHarness.Core.Hosting;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Cli;

public sealed partial class TerminalUi
{
    async Task Queue(int id)
    {
        var items = (await client.Call("inbox.list", new { chatId = id }))!.AsArray();
        var key = await Prompt("File d’attente / Queue", choices: items.Select(q => new Choice(I(q, "id").ToString(), S(q, "text"), S(q, "mode"))).Prepend(new("resume", "Reprendre la file / Resume queue")).ToList());
        if (key == null) return;
        if (key == "resume") { Post(() => { halted.Remove(id); if (!Running(id)) StartRun(id, async () => { await client.Call("inbox.resume", new { chatId = id }, lifetime.Token); }); }); return; }
        int itemId = int.Parse(key); var item = items.Single(q => I(q, "id") == itemId)!;
        var action = await Prompt("Message #" + key, S(item, "text"), [new("edit", "Modifier / Edit"), new("steer", "Injecter dans le tour actif / Steer"), new("delete", "Supprimer / Delete")]);
        if (action == "delete") await client.Call("inbox.delete", new { id = itemId, chatId = id });
        if (action is "edit" or "steer")
        {
            var text = action == "edit" ? await Prompt("Modifier / Edit", initial: S(item, "text")) : S(item, "text");
            if (!string.IsNullOrWhiteSpace(text)) await client.Call("inbox.update", new { id = itemId, chatId = id, expectedText = S(item, "text"), text, steer = action == "steer" });
        }
    }
    async Task QueueAction(int id, int itemId, string action)
    {
        var items = (await client.Call("inbox.list", new { chatId = id }))!.AsArray();
        var item = items.FirstOrDefault(q => I(q, "id") == itemId);
        if (item == null) return;
        if (action == "delete") await client.Call("inbox.delete", new { id = itemId, chatId = id });
        else
        {
            var text = action == "edit" ? await Prompt("Modifier / Edit", initial: S(item, "text")) : S(item, "text");
            if (!string.IsNullOrWhiteSpace(text)) await client.Call("inbox.update", new { id = itemId, chatId = id, expectedText = S(item, "text"), text, steer = action == "steer" });
        }
    }
    async Task Agents(int id, Chat chat)
    {
        var children = (await client.Call("subagents", new { chatId = id }))!.Deserialize<List<SubagentRecord>>(HarnessService.Json)!;
        var key = await Prompt("Sous-agents / Subagents", "Mode : " + chat.OrchestrationMode, children.Select(c => new Choice(c.Id, c.Name, c.Status + " · " + c.Activity)).Prepend(new("mode", "Orchestration : Disabled / Auto / Forced")).ToList());
        if (key == "mode") { var mode = await Prompt("Orchestration", choices: [new("disabled", "Désactivée / Disabled"), new("auto", "Auto"), new("forced", "Forcée / Forced")]); if (mode != null) { await client.Call("chat.modes", new { id, orchestrationMode = mode }); await Refresh(); } }
        else if (key != null)
        {
            var child = children.Single(c => c.Id == key);
            var transcript = JsonNode.Parse(child.TranscriptJson)!.AsArray();
            await Show(child.Name + " · " + child.Status, child.Task + "\n\n" + string.Join("\n\n", transcript.Select(m => S(m, "role").ToUpperInvariant() + "\n" + (m?["content"] is JsonValue value && value.TryGetValue<string>(out var content) ? content : m?.ToJsonString()))));
        }
    }
    async Task Git(int id, int projectId)
    {
        var data = await client.Call("git.files", new { chatId = id, projectId }); var files = data!["files"]!.Deserialize<List<GitChangedFile>>(HarnessService.Json)!;
        if (files.Count == 0) { await Show("Git", L("Aucun fichier modifié (ou aucun dépôt .git attaché).", "No changed files (or no attached .git repository).")); return; }
        var key = await Prompt("Git · " + files.Count, choices: files.Select((f, i) => new Choice(i.ToString(), f.Path, $"{f.Status} +{f.Added ?? 0} −{f.Removed ?? 0}")).ToList());
        if (key == null) return; var file = files[int.Parse(key)];
        var diff = await client.Call("git.diff", new { chatId = id, projectId, repository = file.Repository, path = file.Path }); await Show(file.Path, diff!.GetValue<string>());
    }
    async Task Terminal(int id, string command)
    {
        if (command.Length > 0)
        {
            var terminal = await client.Call("terminals.create", new { chatId = id, name = TerminalText.Fit(command, 40) });
            await client.Call("terminals.start", new { chatId = id, terminalId = S(terminal, "id"), command });
        }
        while (true)
        {
            var terminals = (await client.Call("terminals.list", new { chatId = id }))!.Deserialize<List<TerminalHub.View>>(HarnessService.Json)!;
            var key = await Prompt("Terminaux / Terminals", "/terminal commande : lancer en arrière-plan / start in background",
                terminals.Select(t => new Choice(t.Id, t.Name, t.Status)).ToList());
            if (key == null) return;
            var terminal = terminals.Single(t => t.Id == key);
            var action = await Prompt(terminal.Name + " · " + terminal.Status, terminal.Directory + "\n$ " + terminal.Command + "\n\n" + terminal.Output,
                [new("refresh", "Actualiser / Refresh"), new("stop", "Arrêter / Stop"), new("delete", "Fermer / Close terminal")]);
            if (action == "stop") await client.Call("terminals.stop", new { chatId = id, terminalId = key });
            if (action == "delete") await client.Call("terminals.delete", new { chatId = id, terminalId = key });
            if (action == null) return;
        }
    }
    async Task Memory(int projectId, int id, string query)
    {
        var store = new MemoryStore(client.Database); var access = new MemoryAccess(projectId, id);
        var page = await store.SearchAsync(access, query, ct: lifetime.Token);
        var key = await Prompt("Mémoire / Memory", "/memory mot-clé · 20 résultats maximum", page.Items.Select(m => new Choice(m.Id.ToString(), m.Title, m.Scope + " / " + m.Category)).ToList());
        if (key != null) { var entry = await store.ReadAsync(access, int.Parse(key), lifetime.Token); await Show(entry.Title, entry.Content); }
    }
    async Task Mcp(WorkspaceSnapshot snapshot)
    {
        var key = await Prompt("MCP", "MCP.json : " + McpConfigFile.FilePath, snapshot.Servers.Select(s => new Choice(s.Id.ToString(), (s.Enabled ? "[x] " : "[ ] ") + s.Name)).Prepend(new("import", "Importer un fichier JSON / Import JSON file")).ToList());
        if (key == null) return;
        if (key == "import")
        {
            var path = await Prompt("Fichier MCP JSON / MCP JSON file"); if (string.IsNullOrWhiteSpace(path)) return;
            var current = await client.Call("mcp.json.get"); var content = await File.ReadAllTextAsync(Path.GetFullPath(path.Trim('"')), lifetime.Token);
            if (await Prompt("Remplacer MCP.json / Replace MCP.json?", content, [new("no", "Annuler / Cancel"), new("yes", "Importer / Import")]) == "yes")
                await client.Call("mcp.json.save", new { content, expected = S(current, "content") });
        }
        else { int id = int.Parse(key); await client.Call("mcp.toggle", new { id, enabled = !snapshot.Servers.Single(s => s.Id == id).Enabled }); }
        await Refresh();
    }
    async Task Sandbox(int id, Chat chat)
    {
        var action = await Prompt("Sandbox", L("Docker/Podman requis. Les modifications restent dans une copie isolée jusqu’à validation.", "Requires Docker/Podman. Changes stay in an isolated copy until approved."), [new("toggle", chat.SandboxEnabled ? "Désactiver / Disable" : "Activer / Enable"), new("review", "Examiner les modifications / Review changes")]);
        if (action == "toggle") { await client.Call("chat.modes", new { id, sandboxEnabled = !chat.SandboxEnabled }); await Refresh(); }
        if (action == "review")
        {
            var review = await client.Call("sandbox.review", new { chatId = id });
            var token = S(review, "token");
            try
            {
                var decision = await Prompt("Sandbox · " + I(review, "count"), S(review, "diff"),
                    [new("close", "Fermer / Close"), new("apply", "Appliquer au projet réel / Apply to real project")]);
                if (decision == "apply") await client.Call("sandbox.apply", new { chatId = id, token });
            }
            finally { await client.Call("sandbox.close", new { token }); }
        }
    }
    async Task Settings(WorkspaceSnapshot snapshot)
    {
        var action = await Prompt("Réglages / Settings", client.Database, [new("language", "Langue / Language"), new("theme", "Thème / Theme"), new("font", "Police et CRT / Font and CRT"), new("thinking", "Réflexion / Thinking"), new("style", "Style de réponse / Response style"), new("permissions", "Autorisations / Permissions"), new("continue", "Auto-continue : " + snapshot.State.AutoContinue), new("naming", "Nommage des conversations / Conversation naming"), new("vision", "Bypass image AI"), new("logs", "Logs"), new("updates", "Mises à jour GitHub / GitHub updates")]);
        if (action == "style")
        {
            var current = FeatureSettings.Read(snapshot.State.FeaturesJson).ResponseStyle;
            var value = await Prompt(L("Style de réponse", "Response style"), L("Appliqué aux prochains envois. DEFAULT conserve le comportement habituel.", "Applies to subsequent messages. DEFAULT keeps the usual behavior."),
                ResponseStyles.All.Select(style => new Choice(style.Id, L(style.French, style.English), style.Id == current ? "✓" : "")).ToList());
            if (value != null) await client.State(s => { var settings = FeatureSettings.Read(s.FeaturesJson); settings.ResponseStyle = value; s.FeaturesJson = settings.Json(); });
        }
        if (action == "updates") await UpdateSettings();
        if (action == "naming") await NamingSettings(snapshot);
        if (action == "font") await ConfigureTerminalFont(snapshot);
        if (action == "vision") await VisionSettings(snapshot);
        if (action == "logs") await LogSettings(snapshot);
        if (action == "continue") await client.State(s => s.AutoContinue = !s.AutoContinue);
        if (action == "language") { var value = await Prompt("Language", choices: [new("fr", "Français"), new("en", "English")]); if (value != null) await client.State(s => s.Language = value); }
        if (action == "thinking") { var value = await Prompt("Thinking", choices: new[] { "auto", "low", "medium", "high", "none" }.Select(s => new Choice(s, s)).ToList()); if (value != null) await client.State(s => s.ThinkingLevel = value); }
        if (action == "theme") await ChooseCliTheme(snapshot);
        if (action == "permissions")
        {
            var value = await Prompt("Permissions", "Ce réglage s’applique au workspace partagé / Applies to the shared workspace.", [new("ask", "Demander / Ask"), new("deny", "Tout refuser / Deny all"), new("allow", "Tout accepter / Allow all")]);
            if (value != null) await client.State(s => s.PermissionMode = value);
        }
        await Refresh();
    }
}

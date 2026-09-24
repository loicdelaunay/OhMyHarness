using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using OhMyHarness.Core.Hosting;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Cli;

public sealed partial class TerminalUi
{
    static readonly Choice[] Commands =
    [
        new("/help", "Commandes et raccourcis / Commands and shortcuts", "Ctrl+P"),
        new("/new", "Nouvelle conversation / New chat", "Ctrl+N"),
        new("/chats", "Rechercher une conversation / Find chat", "Ctrl+O"),
        new("/favorite", "Favori / Favorite", "Épingler ou détacher cette conversation"),
        new("/name", "Nommer avec l’IA / Name with AI"),
        new("/project", "Projet / Project", "Choisir ou attacher un dossier"),
        new("/models", "Modèles / Models", "Ctrl+B · choisir / actualiser"),
        new("/providers", "Fournisseurs / Providers", "Connexions et clés API"),
        new("/connect", "Ajouter un fournisseur / Add provider"),
        new("/attach", "Joindre des sources / Attach sources", "/attach chemin"),
        new("/image", "Joindre une image / Attach image", "/image chemin.png"),
        new("/clear-images", "Retirer les images jointes / Clear attached images"),
        new("/mode", "Plan / Exécution", "Protection technique des écritures"),
        new("/agents", "Sous-agents / Subagents", "Disabled · Auto · Forced"),
        new("/skills", "Skills", "Activer / désactiver"),
        new("/mcp", "Serveurs MCP / MCP servers", "MCP.json · activations"),
        new("/context", "Contexte / Context", "Tokens et répartition"),
        new("/compact", "Compacter le contexte / Compact context"),
        new("/queue", "Messages en attente / Queued messages", "Modifier · supprimer · guider"),
        new("/steer", "Guider le tour actif / Steer", "/steer instruction"),
        new("/tasks", "Liste des tâches / Task list"),
        new("/git", "Modifications Git / Git changes", "Fichiers + diff"),
        new("/terminal", "Terminaux / Terminals", "/terminal commande"),
        new("/memory", "Mémoire / Memory", "/memory recherche"),
        new("/templates", "Templates", "Préremplir le message"),
        new("/fork", "Créer un fork / Fork chat", "Depuis un message"),
        new("/export", "Exporter Markdown / Export Markdown"),
        new("/sandbox", "Sandbox", "Activer · examiner"),
        new("/details", "Raisonnement et outils / Reasoning and tools", "Développer / réduire"),
        new("/settings", "Réglages / Settings", "Langue · thème · réflexion"),
        new("/theme", "Thème du CLI / CLI theme", "CRT vert · ambre · néon"),
        new("/font", "Police et CRT / Font and CRT", "Profil Windows Terminal"),
        new("/stop", "Arrêter ce tour / Stop this turn", "Échap"),
        new("/quit", "Quitter / Quit", "Ctrl+Q")
    ];

    void Command(string line)
    {
        var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLowerInvariant(), argument = parts.Length > 1 ? parts[1].Trim().Trim('"') : "";
        if (command is "/quit" or "/exit") { RequestQuit(); return; }
        if (command is "/commands" or "/help" or "/")
        {
            Work(async () => { var selected = await Prompt("OhMyHarness · " + L("Commandes", "Commands"), "Ctrl+J / Alt+Entrée : nouvelle ligne · PgUp/PgDn : historique", Commands.ToList()); if (selected != null) Post(() => Command(selected)); }); return;
        }
        if (workspace == null || CurrentChat == null) { notice = L("Chargement en cours…", "Still loading…"); return; }
        int id = chatId, selectedProvider = providerId, projectId = CurrentChat.ProjectId;
        var snapshot = workspace; var selectedChat = CurrentChat; var project = CurrentProject!;
        switch (command)
        {
            case "/theme": Work(() => ChooseCliTheme(snapshot, argument)); break;
            case "/font": Work(() => ConfigureTerminalFont(snapshot)); break;
            case "/details": showDetails = !showDetails; break;
            case "/stop": halted.Add(id); Work(async () => { await client.Call("stop", new { chatId = id }); }); break;
            case "/new": Work(async () => { int created = await client.CreateChat(projectId, lifetime.Token); await Refresh(); Post(() => SwitchChat(created)); }); break;
            case "/chats":
                Work(async () =>
                {
                    var selected = await Prompt(L("Conversations · recherchez un titre", "Conversations · search a title"), choices: snapshot.Chats.Select(c => new Choice(c.Id.ToString(), c.Title, snapshot.Projects.FirstOrDefault(p => p.Id == c.ProjectId)?.Name ?? "")).ToList());
                    if (selected != null) Post(() => SwitchChat(int.Parse(selected)));
                }); break;
            case "/project": Work(() => Projects(snapshot, argument)); break;
            case "/favorite": Work(async () => { await client.Call("chat.favorite", new { id, favorite = !selectedChat.IsFavorite }); await Refresh(); }); break;
            case "/name": Work(async () => { await client.Call("chat.autoname", new { id }); await Refresh(); }); break;
            case "/connect": Work(() => Connect()); break;
            case "/providers": Work(() => Providers(snapshot)); break;
            case "/models": Work(() => Models(snapshot, selectedProvider)); break;
            case "/mode":
                Work(async () =>
                {
                    var mode = await Prompt("Mode", L("Plan bloque les outils de modification. Exécution autorise les outils selon les skills et permissions.", "Plan blocks modifying tools. Execution uses your enabled skills and permissions."), [new("plan", "Plan"), new("execute", "Exécution / Execution")]);
                    if (mode != null) { await client.Call("chat.modes", new { id, executionMode = mode }); await Refresh(); }
                }); break;
            case "/agents": Work(() => Agents(id, selectedChat)); break;
            case "/skills":
                Work(async () =>
                {
                    var skills = Skills.Available(project.GetSourceFolders(), projectId).Where(s => !CliClient.DesktopSkills.Contains(s.Id));
                    var selected = await Prompt("Skills", L("Les outils graphiques sont disponibles via MCP dans le CLI.", "Graphical tools are available through MCP in the CLI."), skills.Select(s => new Choice(s.Id, (Skills.Enabled(snapshot.State.EnabledSkills, s.Id) ? "[x] " : "[ ] ") + (snapshot.State.Language == "en" ? s.EnglishName : s.FrenchName))).ToList());
                    if (selected == null) return;
                    await client.State(state => { var ids = state.EnabledSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); if (!ids.Remove(selected)) ids.Add(selected); state.EnabledSkills = string.Join(',', ids); });
                    await Refresh(); Post(() => Command("/skills"));
                }); break;
            case "/attach":
                Work(async () =>
                {
                    var path = argument.Length > 0 ? argument : await Prompt(L("Dossier ou fichier source", "Source file or folder")); if (string.IsNullOrWhiteSpace(path)) return;
                    path = Path.GetFullPath(path.Trim('"'));
                    if (!Directory.Exists(path) && !File.Exists(path)) throw new FileNotFoundException(path);
                    var paths = ProjectResources.Effective(selectedChat, project).GetSourceFolders().Append(path).Distinct(PlatformSupport.PathComparer).ToArray();
                    await client.Call("chat.resources", new { id, paths }); await Refresh(); Post(() => notice = L("Source attachée : ", "Source attached: ") + path);
                }); break;
            case "/image":
                Work(async () =>
                {
                    var path = argument.Length > 0 ? argument : await Prompt("Image · PNG / JPEG / WebP"); if (string.IsNullOrWhiteSpace(path)) return;
                    path = Path.GetFullPath(path.Trim('"')); var file = new FileInfo(path);
                    string mime = file.Extension.ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => throw new ArgumentException("PNG, JPEG or WebP required.") };
                    if (file.Length > 8 * 1024 * 1024) throw new ArgumentException("8 MB maximum.");
                    var image = new Attachment { Name = file.Name, Mime = mime, Data = await File.ReadAllBytesAsync(path, lifetime.Token) };
                    Post(() => { if (!attachments.TryGetValue(id, out var images)) attachments[id] = images = []; if (images.Count < 4) images.Add(image); else notice = "4 images maximum · /clear-images"; });
                }); break;
            case "/clear-images": attachments[id] = []; break;
            case "/steer":
                if (argument.Length == 0) { editor.Set("/steer "); notice = L("Ajoutez votre instruction après /steer.", "Add an instruction after /steer."); }
                else { editor.Set(argument); Send(true); }
                break;
            case "/queue": Work(() => Queue(id)); break;
            case "/context":
                Work(async () =>
                {
                    var context = (await client.Call("context.details", new { chatId = id, providerId = selectedProvider }))!.Deserialize<ContextDetails>(HarnessService.Json)!;
                    await Show("Contexte / Context", $"{context.Used:N0} / {context.Limit:N0} tokens {(context.Estimated ? "≈" : "")}\n\nUser: {context.User:N0}\nAssistant: {context.Assistant:N0}\nTools: {context.Tools:N0}\nSummary: {context.Summary:N0}\nImages: {context.Images:N0}\nActive messages: {context.ActiveMessages}\nArchived: {context.ArchivedMessages}\n\n/compact : compactage manuel / compact manually");
                }); break;
            case "/compact": if (!Running(id)) StartRun(id, async () => { await client.Call("context.compact", new { chatId = id, providerId = selectedProvider }, lifetime.Token); }); break;
            case "/git": Work(() => Git(id, projectId)); break;
            case "/terminal": Work(() => Terminal(id, argument)); break;
            case "/memory": Work(() => Memory(projectId, id, argument)); break;
            case "/mcp": Work(() => Mcp(snapshot)); break;
            case "/tasks":
                var todo = View(id).Messages.Values.LastOrDefault(m => m.Role == "tasks");
                Work(() => Show("TODO", todo?.Content ?? L("Aucune tâche pour le moment.", "No tasks yet."))); break;
            case "/templates":
                Work(async () => { await using var db = client.OpenDb(); var templates = await db.Templates.AsNoTracking().ToListAsync(); var key = await Prompt("Templates", choices: templates.Select(t => new Choice(t.Id.ToString(), t.Name)).ToList()); if (key != null) Post(() => editor.Set(templates.Single(t => t.Id == int.Parse(key)).Content)); }); break;
            case "/fork":
                Work(async () => { var history = await client.History(id, lifetime.Token); var selected = await Prompt("Fork", choices: history.Where(m => m.Role is "user" or "assistant" && m.State is "complete" or "compacted").Select(m => new Choice(m.Id.ToString(), m.Role + " · " + TerminalText.Fit(m.Content, 90))).ToList()); if (selected == null) return; var fork = await client.Call("chat.branch", new { chatId = id, messageId = int.Parse(selected), resume = false }); await Refresh(); Post(() => SwitchChat(I(fork, "id"))); }); break;
            case "/export":
                Work(async () =>
                {
                    var export = (await client.Call("chat.export", new { chatId = id, providerId = selectedProvider }))!.Deserialize<ConversationExport.Document>(HarnessService.Json)!;
                    var path = argument.Length > 0 ? argument : await Prompt("Export Markdown", initial: Path.Combine(PortableStorage.Root, "exports", export.FileName));
                    if (string.IsNullOrWhiteSpace(path)) return; path = Path.GetFullPath(path);
                    if (File.Exists(path) && await Prompt("Remplacer / Replace?", path, [new("no", "Non / No"), new("yes", "Oui / Yes")]) != "yes") return;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, export.Markdown, lifetime.Token); Post(() => notice = "Export → " + path);
                }); break;
            case "/sandbox": Work(() => Sandbox(id, selectedChat)); break;
            case "/settings": Work(() => Settings(snapshot)); break;
            default: notice = L("Commande inconnue : ", "Unknown command: ") + command + " · /help"; break;
        }
    }
    Task Show(string title, string content) => Prompt(title, content, [new("close", "Fermer / Close")]);

    async Task Projects(WorkspaceSnapshot snapshot, string argument)
    {
        var value = argument.Length > 0 ? "folder" : await Prompt("Projets / Projects", choices: snapshot.Projects.Select(p => new Choice(p.Id.ToString(), p.Name)).Append(new("folder", "+ Ouvrir un dossier / Open folder")).ToList());
        if (value == null) return;
        if (value == "folder")
        {
            string? folder = argument.Length > 0 ? argument : await Prompt("Dossier du projet / Project folder", initial: Environment.CurrentDirectory);
            if (string.IsNullOrWhiteSpace(folder)) return;
            folder = Path.GetFullPath(folder.Trim('"')); if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            var result = await client.Initialize(new CliOptions { Directory = folder }, lifetime.Token);
            Post(() => { workspace = result.Snapshot; SwitchChat(result.ChatId); });
        }
        else
        {
            int projectId = int.Parse(value); var chat = snapshot.Chats.FirstOrDefault(c => c.ProjectId == projectId);
            int selected = chat?.Id ?? await client.CreateChat(projectId, lifetime.Token); await Refresh(); Post(() => SwitchChat(selected));
        }
    }
    string connectionProgress = "";
    async Task Connect()
    {
        try
        {
            using var http = new HttpClient();
            var wizard = new ProviderConnectionWizard(http, step => Prompt(step.Title, step.Body, step.Choices, step.Secret, step.Initial, lifetime.Token),
                client.SaveProvider, message => Post(() => connectionProgress = message));
            var added = await wizard.RunAsync(lifetime.Token);
            if (added == null) { Post(() => notice = L("Connexion annulée", "Connection cancelled")); return; }
            await Refresh(); Post(() => { providerId = added.Value; View(chatId).Limit = 0; notice = L("Fournisseur et modèles enregistrés", "Provider and models saved"); });
        }
        finally { Post(() => connectionProgress = ""); }
    }
    async Task Providers(WorkspaceSnapshot snapshot)
    {
        var selected = await Prompt("Fournisseurs / Providers", choices: snapshot.Providers.Select(p => new Choice(p.Id.ToString(), p.Name, p.Model)).Append(new("new", "+ Ajouter / Add")).ToList());
        if (selected == "new") { await Connect(); return; } if (selected == null) return;
        int id = int.Parse(selected); var provider = snapshot.Providers.Single(p => p.Id == id);
        var action = await Prompt(provider.Name, provider.BaseUrl, [new("models", "Modèles / Models"), new("key", "Modifier la clé / Change key"), new("delete", "Supprimer / Delete")]);
        if (action == "models") { await Models(snapshot, id); return; }
        if (action == "key")
        {
            var key = await Prompt("Clé API / API key", "Vide = supprimer la clé / Empty = remove key", secret: true); if (key == null) return;
            await using var db = client.OpenDb(); var entity = await db.Providers.SingleAsync(p => p.Id == id); entity.ProtectedKey = key.Length == 0 ? [] : KeyVault.Encrypt(key); await db.SaveChangesAsync();
        }
        if (action == "delete" && await Prompt("Supprimer / Delete?", provider.Name, [new("no", "Annuler / Cancel"), new("yes", "Supprimer / Delete")]) == "yes") await client.Call("provider.delete", new { id });
        await Refresh();
    }
    async Task Models(WorkspaceSnapshot snapshot, int selectedProvider)
    {
        var models = snapshot.Providers.SelectMany(p => ProviderModels.Visible(p).Select(model => (Provider: p.Id, Model: model, Name: p.Name))).ToList();
        var selected = await Prompt("Modèles / Models", L("Tapez pour filtrer. Actualiser détecte et sélectionne les modèles du fournisseur courant.", "Type to filter. Refresh detects and selects the current provider’s models."), models.Select((m, i) => new Choice(i.ToString(), m.Model, m.Name)).Prepend(new("refresh", "↻ Actualiser / Refresh", snapshot.Providers.FirstOrDefault(p => p.Id == selectedProvider)?.Name ?? "")).ToList());
        if (selected == "refresh") { await client.RefreshModels(selectedProvider, lifetime.Token); await Refresh(); Post(() => Command("/models")); return; }
        if (selected == null) return; var model = models[int.Parse(selected)]; await client.SelectModel(model.Provider, model.Model, lifetime.Token); await Refresh(); Post(() => { providerId = model.Provider; View(chatId).Limit = 0; });
    }
}

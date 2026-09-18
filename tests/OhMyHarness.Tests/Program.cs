using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("ÉCHEC : " + name); Console.WriteLine("OK : " + name); passed++; }
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{ try { await action(); } catch (T) { Check(true, name); return; } throw new Exception("Exception attendue : " + name); }
string Event(object value) => "data: " + System.Text.Json.JsonSerializer.Serialize(value) + "\r\n\r\n";
var stream = ": heartbeat\r\n\r\n" + Event(new { choices = new[] { new { delta = new { content = "Bonjour " } } } })
    + Event(new { choices = new[] { new { delta = new { content = "世界" } } } })
    + Event(new { choices = Array.Empty<object>(), usage = new { prompt_tokens = 87, completion_tokens = 4 } }) + "data: [DONE]\r\n\r\n";
var complete = await ChatEngine.ParseStreamAsync(new StringReader(stream), _ => { }, default);
Check(complete.Message["content"]!.GetValue<string>() == "Bonjour 世界", "Streaming Unicode et CRLF");
Check(complete.InputTokens == 87 && complete.OutputTokens == 4, "Usage fournisseur en fin de flux");
var toolStream = Event(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, id = "call_1", function = new { name = "read_source", arguments = "{\"path\":" } } } } } } })
    + Event(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, function = new { arguments = "\"a.cs\"}" } } } } } } }) + "data: [DONE]\n\n";
var toolResult = await ChatEngine.ParseStreamAsync(new StringReader(toolStream), _ => { }, default);
Check(toolResult.Message["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>() == "{\"path\":\"a.cs\"}", "Assemblage des arguments d’outil fragmentés");
await Throws<IOException>(() => ChatEngine.ParseStreamAsync(new StringReader(Event(new { choices = Array.Empty<object>() })), _ => { }, default), "Flux tronqué détecté");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    await Throws<OperationCanceledException>(() => ChatEngine.ParseStreamAsync(new StringReader(stream), _ => { }, cancellation.Token), "Annulation du streaming");
}
Check(ChatEngine.Endpoint("https://example.com/v1", "chat/completions").AbsoluteUri == "https://example.com/v1/chat/completions", "Route OpenAI v1");
Check(MouseInput.NormalizeButton(null) == "left" && MouseInput.NormalizeButton("RIGHT") == "right", "Clics souris gauche et droit normalisés");
await Throws<ArgumentException>(() => Task.FromResult(MouseInput.NormalizeButton("middle")), "Bouton souris non autorisé refusé");
Check(MouseInput.NormalizeClickCount(1) == 1 && MouseInput.NormalizeClickCount(2) == 2, "Simple et double clic souris acceptés");
await Throws<ArgumentOutOfRangeException>(() => Task.FromResult(MouseInput.NormalizeClickCount(3)), "Nombre de clics souris invalide refusé");
Check(MouseInput.ToWindowsWheelDelta(240) == -240 && MouseInput.ToWindowsWheelDelta(-120) == 120, "Sens de défilement souris cohérent entre navigateur et Windows");
Check(PermissionModes.AutomaticDecision("deny") == false && PermissionModes.AutomaticDecision("allow") == true && PermissionModes.AutomaticDecision("ask") == null, "Politique globale des autorisations appliquée avant les dialogues");
Check(PermissionModes.Normalize("inconnu") == PermissionModes.Ask, "Politique d’autorisation invalide ramenée au mode Demander");
Check(HarnessDb.DatabasePath == Path.Combine(AppContext.BaseDirectory, "database.sqlite"), "Base SQLite par défaut placée à côté de l’exécutable");
var saveChord = KeyboardInput.ParseChord("ctrl+s");
Check(saveChord.Modifiers.SequenceEqual(["CTRL"]) && saveChord.Key == "S", "Raccourci clavier CTRL+S normalisé");
var aliasChord = KeyboardInput.ParseChord("control+return");
Check(aliasChord.Modifiers.SequenceEqual(["CTRL"]) && aliasChord.Key == "ENTER", "Alias clavier normalisés");
Check(KeyboardInput.ParseChord("ALT+TAB").Key == "TAB" && KeyboardInput.ParseChord("WIN+D").Key == "D" && KeyboardInput.ParseChord("F12").Key == "F12", "Touches spéciales clavier acceptées");
await Throws<ArgumentException>(() => Task.FromResult(KeyboardInput.ParseChord("CTRL+S+Q")), "Raccourci avec plusieurs touches principales refusé");
await Throws<ArgumentException>(() => Task.FromResult(KeyboardInput.ParseChord("")), "Raccourci clavier vide refusé");
var contextSample = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(
    new JsonObject { ["type"] = "text", ["text"] = new string('a', 400) },
    new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + new string('A', 20_000) } }) });
Check(ContextWindow.Estimate(contextSample) is >= 100 and < 5000 && ContextWindow.ShouldCompact(950, 1000) && !ContextWindow.ShouldCompact(949, 1000), "Estimation du contexte et seuil de compaction à 95 %");
using (var client = new HttpClient(new FakeHandler(async request =>
{
    Check(request.RequestUri!.AbsoluteUri == "https://example.com/v1/chat/completions", "Requête HTTP vers le bon endpoint");
    Check(request.Headers.Authorization?.ToString() == "Bearer test-key", "Authentification Bearer");
    var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
    Check(body["stream_options"]!["include_usage"]!.GetValue<bool>() && body["tools"]!.AsArray().Count == 2, "Payload streaming avec usage et outils");
    return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(stream) };
})))
{
    var result = await new ChatEngine(client).StreamAsync(new Provider { BaseUrl = "https://example.com/v1" }, "test-key",
        new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Bonjour" }), ChatEngine.ToolDefinitions(true, false), _ => { }, default);
    Check(result.OutputTokens == 4, "Client HTTP complet avec fournisseur simulé");
}
using (var client = new HttpClient(new FakeHandler(async request =>
{
    var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
    Check(body["reasoning_effort"]?.GetValue<string>() == "high", "Paramètre reasoning_effort transmis dans le payload");
    return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(stream) };
})))
{
    await new ChatEngine(client).StreamAsync(new Provider { BaseUrl = "https://example.com/v1" }, "test-key",
        new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Bonjour" }), new JsonArray(), _ => { }, default, "high");
}
using (var client = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)))))
    await Throws<HttpRequestException>(() => new ChatEngine(client).StreamAsync(new Provider(), "invalid", new JsonArray(), new JsonArray(), _ => { }, default), "Erreur API 401 remontée");
await Throws<ArgumentException>(() => Task.FromResult(ChatEngine.Endpoint("http://example.com", "models")), "Clé API refusée sur HTTP distant");
var openCodeProvider = new Provider { Kind = "opencode", BaseUrl = "http://127.0.0.1:4096", Username = "opencode", Model = "test/coder" };
using (var client = new HttpClient(new FakeHandler(request =>
{
    Check(request.Headers.Authorization?.Scheme == "Basic", "Authentification Basic du serveur OpenCode");
    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"healthy\":true,\"version\":\"1.2.3\"}") });
})))
    Check(await new OpenCodeEngine(client).HealthAsync(openCodeProvider, "secret", default) == "1.2.3", "Santé du serveur OpenCode détectée");
using (var client = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
{
    Content = new StringContent("{\"all\":[{\"id\":\"test\",\"models\":{\"coder\":{\"name\":\"Coder\",\"limit\":{\"context\":64000},\"capabilities\":{\"input\":[\"text\",\"image\"]}}}}],\"connected\":[\"test\"]}")
}))))
{
    var models = await new OpenCodeEngine(client).ModelsAsync(openCodeProvider, "", null, default);
    Check(models.Count == 1 && models[0].Reference == "test/coder" && models[0].ContextLimit == 64000 && models[0].SupportsImages, "Catalogue des modèles OpenCode parsé");
}
var openCodeMessageReads = 0;
using (var client = new HttpClient(new FakeHandler(request =>
{
    var path = request.RequestUri!.AbsolutePath;
    if (path.EndsWith("/experimental/tool/ids")) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[\"bash\",\"read\"]") });
    if (path.EndsWith("/prompt_async")) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
    if (path.EndsWith("/message"))
    {
        openCodeMessageReads++;
        var json = openCodeMessageReads == 1 ? "[]" : "[{\"info\":{\"id\":\"msg-a\",\"role\":\"assistant\",\"time\":{\"completed\":1},\"tokens\":{\"input\":21,\"output\":4}},\"parts\":[{\"type\":\"reasoning\",\"text\":\"Analyse\"},{\"type\":\"text\",\"text\":\"Bonjour\"}]}]";
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
    }
    throw new InvalidOperationException(path);
})))
{
    var updates = new List<GenerationUpdate>();
    var result = await new OpenCodeEngine(client).PromptAsync(openCodeProvider, "", "C:\\Projet", "ses-1", "Salut", "Système", [], updates.Add, default);
    Check(result.Message["content"]!.GetValue<string>() == "Bonjour" && result.InputTokens == 21 && result.OutputTokens == 4 && updates.Last().Reasoning == "Analyse", "Réponse OpenCode et compteurs récupérés par polling");
}
var workspace = Path.Combine(Path.GetTempPath(), "OhMyHarness-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
try
{
    var sources = Path.Combine(workspace, "sources"); Directory.CreateDirectory(sources);
    await File.WriteAllTextAsync(Path.Combine(sources, "a.cs"), "class Example {}");
    await File.WriteAllTextAsync(Path.Combine(sources, ".env"), "secret");
    await File.WriteAllTextAsync(Path.Combine(workspace, "outside.cs"), "outside");
    var access = new SourceAccess(sources);
    Check((await access.ReadAsync("a.cs", default)).Contains("Example"), "Lecture source autorisée");
    Check(!access.List().Contains(".env"), "Secrets exclus de la liste");
    await access.WriteAsync("sub/b.cs", "class B {}", default);
    Check((await access.ReadAsync("sub/b.cs", default)) == "class B {}", "Écriture de fichier source autorisée");
    await access.ModifyAsync("sub/b.cs", "class B", "class BModified", default);
    Check((await access.ReadAsync("sub/b.cs", default)) == "class BModified {}", "Modification chirurgicale de fichier source autorisée");
    await Throws<InvalidOperationException>(() => access.ModifyAsync("sub/b.cs", "nonexistent", "foo", default), "Modification échoue si texte cible absent");
    await Throws<InvalidOperationException>(() => access.WriteAsync("test.exe", "bin", default), "Écriture d'extension non autorisée bloquée");
    await Throws<UnauthorizedAccessException>(() => access.ReadAsync("../outside.cs", default), "Traversée de répertoire bloquée");
    await Throws<UnauthorizedAccessException>(() => access.ReadAsync(".env", default), "Lecture .env bloquée");

    var proj = new Project();
    Check(proj.GetSourceFolders().Count == 0, "Dossiers sources initialement vides");
    proj.SetSourceFolders(["  C:\\Projects\\Front  ", "C:\\Projects\\Back", "c:\\projects\\front"]);
    Check(proj.GetSourceFolders().Count == 2 && proj.SourceFolder == "C:\\Projects\\Front|C:\\Projects\\Back", "SetSourceFolders normalise et déduplique");
    proj.SourceFolder = "C:\\A;C:\\B\nD:\\C";
    Check(proj.GetSourceFolders().Count == 3 && proj.GetSourceFolders()[2] == "D:\\C", "GetSourceFolders gère séparateurs multiples");

    var front = Path.Combine(workspace, "frontend"); Directory.CreateDirectory(front);
    var back = Path.Combine(workspace, "backend"); Directory.CreateDirectory(back);
    await File.WriteAllTextAsync(Path.Combine(front, "index.html"), "<h1>Frontend</h1>");
    await File.WriteAllTextAsync(Path.Combine(front, "common.json"), "{\"app\":\"front\"}");
    await File.WriteAllTextAsync(Path.Combine(back, "server.cs"), "class Server {}");
    await File.WriteAllTextAsync(Path.Combine(back, "common.json"), "{\"app\":\"back\"}");

    var multiAccess = new SourceAccess([front, back]);
    Check(multiAccess.Roots.Count == 2, "Multi-racines SourceAccess initialisées");
    var rootList = multiAccess.List();
    Check(rootList.Contains("[dossier] frontend") && rootList.Contains("[dossier] backend"), "Listing racine multi-dossiers retourne les alias");
    var frontList = multiAccess.List("frontend");
    Check(frontList.Contains("frontend/index.html") && frontList.Contains("frontend/common.json"), "Listing sous-dossier préfixé par son alias");
    Check((await multiAccess.ReadAsync("frontend/index.html", default)).Contains("Frontend"), "Lecture avec préfixe d'alias");
    Check((await multiAccess.ReadAsync("server.cs", default)).Contains("Server"), "Lecture sans préfixe d'un fichier non-ambigu");
    await Throws<InvalidOperationException>(() => multiAccess.ReadAsync("common.json", default), "Lecture d'un fichier ambigu dans plusieurs dossiers lève une exception explicite");
    await multiAccess.WriteAsync("frontend/style.css", "body { margin: 0; }", default);
    Check((await multiAccess.ReadAsync("frontend/style.css", default)).Contains("margin"), "Écriture dans un dossier source spécifique");
    await multiAccess.ModifyAsync("frontend/style.css", "margin: 0", "margin: 10px", default);
    Check((await multiAccess.ReadAsync("frontend/style.css", default)).Contains("margin: 10px"), "Modification dans un dossier source spécifique");
    await Throws<InvalidOperationException>(() => multiAccess.WriteAsync("ambiguous.txt", "content", default), "Écriture sans préfixe d'alias en multi-racines refusée");

    var path = Path.Combine(workspace, "state.db");
    await using (var upgrade = new HarnessDb(Path.Combine(workspace, "upgrade.db")))
    {
        var first = upgrade.Database.GetMigrations().First();
        await upgrade.GetService<IMigrator>().MigrateAsync(first);
        await upgrade.Database.ExecuteSqlRawAsync("INSERT INTO States (Id, ProviderId, BrowserUrl) VALUES (1, 1, 'https://example.com')");
        await upgrade.Database.MigrateAsync();
        var migrated = await upgrade.States.SingleAsync();
        Check(migrated.Language == "fr" && migrated.EnabledSkills == "sources,web" && migrated.BrowserUrl == "https://example.com" && migrated.ThinkingLevel == "auto" && migrated.PermissionMode == PermissionModes.Ask, "Mise à niveau d’une base existante sans perte d’état");
    }
    await using (var db = new HarnessDb(path))
    {
        await db.InitializeAsync(); await db.InitializeAsync();
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 7, "Migrations et démarrage idempotent");
        var template = await db.Templates.SingleAsync();
        Check(template.Name == "Web app" && template.Content.Contains("index.html"), "Template Web app initial créé par migration");
        template.Content = "Mon template personnalisé";
        var settings = await db.States.SingleAsync();
        settings.Language = "en"; settings.EnabledSkills = "review,planning"; settings.ThinkingLevel = "high"; settings.PermissionMode = PermissionModes.Allow;
        db.PermissionGrants.Add(new PermissionGrant { Scope = "browser-origin|https://example.com", Name = "Test", Details = "example.com" });
        Check(await db.Providers.CountAsync() == 2, "Deux fournisseurs initialisés sans doublons");
        var storedProvider = new Provider { Name = "Compatible local", BaseUrl = "http://localhost:11434/v1", Model = "custom-model", ContextLimit = 32768, SupportsImages = false };
        db.Providers.Add(storedProvider); await db.SaveChangesAsync(); settings.ProviderId = storedProvider.Id;
        var chat = await db.Chats.FirstAsync();
        db.Messages.Add(new Message { ChatId = chat.Id, Content = "image", Attachments = [new Attachment { Data = [1, 2, 3], Name = "test.png" }] });
        await db.SaveChangesAsync();
    }
    await using (var db = new HarnessDb(path))
    {
        var settings = await db.States.SingleAsync();
        Check(settings.Language == "en" && settings.EnabledSkills == "review,planning" && settings.ThinkingLevel == "high" && settings.PermissionMode == PermissionModes.Allow, "Langue, skills, thinking et politique d’autorisation restaurés");
        var selectedProvider = await db.Providers.SingleAsync(x => x.Id == settings.ProviderId);
        Check(await db.Providers.CountAsync() == 3 && selectedProvider.Name == "Compatible local" && selectedProvider.ContextLimit == 32768, "Plusieurs fournisseurs et fournisseur actif restaurés");
        Check(new Provider { Id = 10, Name = "OpenAI" }.ToString() != new Provider { Id = 11, Name = "OpenAI" }.ToString(), "Instances de même nom distinguées dans le sélecteur");
        Check(await db.PermissionGrants.AnyAsync(x => x.Scope == "browser-origin|https://example.com"), "Autorisation permanente restaurée depuis SQLite");
        await db.InitializeAsync();
        Check((await db.Templates.SingleAsync()).Content == "Mon template personnalisé", "Template personnalisé conservé après redémarrage");
        Check(!Skills.Enabled(settings.EnabledSkills, "web") && Skills.Enabled(settings.EnabledSkills, "review"), "Skills activés indépendamment");
        var prompt = Skills.Prompt(settings.EnabledSkills, settings.Language);
        Check(prompt.Contains("Reply in English") && prompt.Contains("When reviewing code") && !prompt.Contains("Use the browser when"), "Seuls les skills actifs sont ajoutés au prompt");
        var promptFrSourcesNoDir = Skills.Prompt("sources,web", "fr", hasSources: false, hasBrowser: false);
        Check(promptFrSourcesNoDir.Contains("bouton 'Sources'") && promptFrSourcesNoDir.Contains("Accès IA au navigateur"), "Prompt d'avertissement lorsque dossier et navigateur désactivés");
        var promptFrSourcesWithDir = Skills.Prompt("sources,web", "fr", hasSources: true, hasBrowser: true);
        Check(promptFrSourcesWithDir.Contains("list_sources") && promptFrSourcesWithDir.Contains("browse"), "Prompt explicite avec noms d'outils quand activés");
        var allTools = ChatEngine.ToolDefinitions(true, true);
        Check(allTools.Count == 4, "4 outils définis lorsque sources et web sont actifs");
        var readPageTool = allTools.First(t => t!["function"]!["name"]!.GetValue<string>() == "read_page");
        Check(readPageTool!["function"]!["parameters"]!["required"]!.AsArray().Count == 0, "read_page sans paramètre requis factice");
        var writeTools = ChatEngine.ToolDefinitions(true, false, writeSources: true);
        Check(writeTools.Count == 4 && writeTools.Any(t => t!["function"]!["name"]!.GetValue<string>() == "write_source") && writeTools.Any(t => t!["function"]!["name"]!.GetValue<string>() == "edit_source"), "Outils write_source et edit_source déclarés");
        var promptWrite = Skills.Prompt("write_sources", "fr", hasSources: true, hasBrowser: false, canWriteSources: true);
        Check(promptWrite.Contains("write_source") && promptWrite.Contains("edit_source") && !promptWrite.Contains("Tools are read-only"), "Prompt dynamique avec permissions d'écriture et modification");
        Check(Skills.All.Any(s => s.Id == "write_sources"), "Skill write_sources disponible dans le catalogue");
        Check(Skills.All.Any(s => s.Id == "mouse_control") && Skills.All.Any(s => s.Id == "keyboard_control") && Skills.All.Any(s => s.Id == "screenshots"), "Skills souris, clavier et capture d’écran disponibles");
        var (w1, h1) = ScreenGeometry.CalculateScaledDimensions(1920, 1080, 1280, null);
        Check(w1 == 1280 && h1 == 720, "ScreenGeometry : réduction proportionnelle par maxWidth");
        var (w2, h2) = ScreenGeometry.CalculateScaledDimensions(1920, 1080, null, 540);
        Check(w2 == 960 && h2 == 540, "ScreenGeometry : réduction proportionnelle par maxHeight");
        var (w3, h3) = ScreenGeometry.CalculateScaledDimensions(1920, 1080, 800, 600);
        Check(w3 == 800 && h3 == 450, "ScreenGeometry : aspect ratio préservé avec double contrainte");
        var testScreens = new List<ScreenInfo>
        {
            new(0, @"\\.\DISPLAY1", false, -1920, 0, 1920, 1080),
            new(1, @"\\.\DISPLAY2", true, 0, 0, 2560, 1440),
            new(2, @"\\.\DISPLAY_PORTRAIT", false, 2560, 0, 1080, 1920)
        };
        Check(ScreenGeometry.ResolveScreen(testScreens, "primary")?.Index == 1, "ScreenGeometry : résolution écran 'primary'");
        Check(ScreenGeometry.ResolveScreen(testScreens, null)?.Index == 1, "ScreenGeometry : résolution par défaut écran principal");
        Check(ScreenGeometry.ResolveScreen(testScreens, "0")?.Index == 0, "ScreenGeometry : résolution écran par index");
        Check(ScreenGeometry.ResolveScreen(testScreens, "PORTRAIT")?.Index == 2, "ScreenGeometry : résolution écran par nom partiel");
        var regCustom = ScreenGeometry.ResolveRegion(testScreens, null, 50, 100, 400, 300, -1920, 0, 5560, 1920);
        Check(regCustom == (50, 100, 400, 300), "ScreenGeometry : résolution zone rectangulaire explicite");
        var regAll = ScreenGeometry.ResolveRegion(testScreens, "all", null, null, null, null, -1920, 0, 5560, 1920);
        Check(regAll == (-1920, 0, 5560, 1920), "ScreenGeometry : résolution bureau complet 'all'");
        var regPrimary = ScreenGeometry.ResolveRegion(testScreens, "primary", null, null, null, null, -1920, 0, 5560, 1920);
        Check(regPrimary == (0, 0, 2560, 1440), "ScreenGeometry : résolution écran ciblé");
        var restored = await db.Messages.Include(x => x.Attachments).SingleAsync();
        Check(restored.Attachments.Single().Data.SequenceEqual(new byte[] { 1, 2, 3 }), "Historique et images restaurés après réouverture");
        var wire = ChatEngine.ToWire(restored);
        Check(wire["content"]![1]!["image_url"]!["url"]!.GetValue<string>().EndsWith("AQID"), "Image sérialisée en contenu multimodal");
        db.Projects.Remove(await db.Projects.SingleAsync()); await db.SaveChangesAsync();
        Check(await db.Messages.CountAsync() == 0 && await db.Set<Attachment>().CountAsync() == 0, "Suppression du projet en cascade");
    }
    var emptyProvidersPath = Path.Combine(workspace, "empty-providers.db");
    await using (var emptyProviders = new HarnessDb(emptyProvidersPath))
    {
        await emptyProviders.InitializeAsync();
        emptyProviders.Providers.RemoveRange(await emptyProviders.Providers.ToListAsync());
        (await emptyProviders.States.SingleAsync()).ProviderId = 0;
        await emptyProviders.SaveChangesAsync();
    }
    await using (var emptyProviders = new HarnessDb(emptyProvidersPath))
    {
        await emptyProviders.InitializeAsync();
        Check(await emptyProviders.Providers.CountAsync() == 0 && (await emptyProviders.States.SingleAsync()).ProviderId == 0, "Zéro à X fournisseurs conservés sans recréation automatique");
    }
    var relocatedPath = Path.Combine(workspace, "database.sqlite");
    await HarnessDb.CopyDatabaseAsync(path, relocatedPath);
    await using (var relocated = new HarnessDb(relocatedPath))
    {
        Check((await relocated.States.SingleAsync()).Language == "en" && (await relocated.Templates.SingleAsync()).Content == "Mon template personnalisé",
            "Copie cohérente de l’ancienne base SQLite vers database.sqlite");
    }
    if (OperatingSystem.IsWindows())
    {
        var encrypted = KeyVault.Encrypt("clé-test");
        Check(KeyVault.Decrypt(encrypted) == "clé-test" && !System.Text.Encoding.UTF8.GetString(encrypted).Contains("clé-test"), "Clé API chiffrée par DPAPI");
        var result = await WorkspaceTools.PowerShellAsync("Write-Output 'terminal-ok'", workspace, default);
        Check(result.Contains("terminal-ok") && result.Contains("Exit code: 0"), "Terminal PowerShell et code de sortie");
        using var cancelCommand = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Throws<OperationCanceledException>(() => WorkspaceTools.PowerShellAsync("Start-Sleep -Seconds 30", workspace, cancelCommand.Token), "Annulation du processus terminal créé");
        Check(!WorkspaceTools.HasGitRepository(workspace), "Outil Git indisponible sans marqueur .git");
        await WorkspaceTools.GitAsync(workspace, ["init", "--quiet"], default);
        Check(WorkspaceTools.HasGitRepository(workspace), "Dépôt Git détecté par son marqueur .git");
        await File.WriteAllTextAsync(Path.Combine(workspace, "git-test.txt"), "tracked");
        await WorkspaceTools.GitAsync(workspace, ["add", "git-test.txt"], default);
        var diff = await WorkspaceTools.GitAsync(workspace, ["--no-pager", "diff", "--cached", "--no-ext-diff", "--no-textconv"], default);
        Check(diff.Contains("+tracked"), "Lecture des changements Git indexés");
        var changes = await WorkspaceTools.GitChangesAsync(workspace, default);
        Check(changes.Contains("MODIFIED FILES") && changes.Contains("CHANGED LINES (STAGED)") && changes.Contains("+tracked"), "Outil Git limité aux modifications et lignes changées");
        Check(LocalPreview.ResolveResource(sources, "a.cs") == Path.Combine(sources, "a.cs"), "Ressource locale autorisée dans le dossier approuvé");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ResolveResource(sources, "%2e%2e/outside.cs")), "Évasion URL encodée bloquée dans l’aperçu");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ResolveResource(sources, ".env")), "Fichiers secrets bloqués dans l’aperçu");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ValidatePath(@"\\server\share\file.html")), "Chemin réseau refusé pour aperçu local");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ValidatePath(Path.Combine(sources, "a.cs:secret"))), "Flux alternatif NTFS refusé");
    }
    var deepseekProvider = new Provider { Name = "DeepSeek", BaseUrl = "https://api.deepseek.com", Model = "deepseek-reasoner" };
    var dsModels = ModelCatalog.GetModelsForProvider(deepseekProvider);
    Check(dsModels.Contains("deepseek-chat") && dsModels.Contains("deepseek-reasoner") && dsModels.Contains("deepseek-r1:7b") && dsModels.Contains("deepseek-r1:70b") && dsModels.Contains("deepseek-coder-v2:16b"), "Catalogue DeepSeek avec sous-modèles complets");
    var customProvider = new Provider { Name = "DeepSeek", BaseUrl = "https://api.deepseek.com", Model = "custom-deepseek-fine-tuned" };
    var customList = ModelCatalog.GetModelsForProvider(customProvider);
    Check(customList[0] == "custom-deepseek-fine-tuned" && customList.Contains("deepseek-chat"), "Modèle personnalisé inclus dans le catalogue");
    var openaiProvider = new Provider { Name = "OpenAI", BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o" };
    var oaiModels = ModelCatalog.GetModelsForProvider(openaiProvider);
    Check(oaiModels.Contains("gpt-4o") && oaiModels.Contains("gpt-4o-mini") && oaiModels.Contains("o1"), "Catalogue OpenAI correctement sélectionné");
    var ocCatalogProvider = new Provider { Name = "OpenCode", Kind = "opencode", BaseUrl = "http://127.0.0.1:4096", Model = "opencode/big-pickle" };
    var ocModels = ModelCatalog.GetModelsForProvider(ocCatalogProvider);
    Check(ocModels.Contains("opencode/big-pickle") && ocModels.Contains("opencode/nemotron-3.5-lightning-free") && ocModels.Contains("opencode/muse-spark-1.2-contributor-free"), "Catalogue OpenCode avec modèles gratuits par défaut");
    Check(ModelCatalog.GetDefaultContextLimit("opencode/big-pickle") == 200_000, "Limite de contexte big-pickle");
    Check(ModelCatalog.GetDefaultContextLimit("opencode/muse-spark-1.2-contributor-free") == 1_048_576, "Limite de contexte muse-spark");
    Check(OpenCodeEngine.IsFreeModel(null, "opencode/big-pickle") && OpenCodeEngine.IsFreeModel(null, "nemotron-3.5-lightning-free"), "Détection des modèles gratuits OpenCode");
    var mdSample = "# Titre 1\n\n```csharp\nint x = 42;\n```\n\n- [x] Fait\n- [ ] A faire\n\n| Nom | Valeur |\n| --- | --- |\n| A | 1 |\n\n[Lien](https://example.com)";
    var mdDoc = MarkdownPipelineHelper.Parse(mdSample);
    Check(mdDoc.OfType<Markdig.Syntax.HeadingBlock>().Any(h => h.Level == 1), "Markdown : Titre H1 parsé");
    var codeBlock = mdDoc.OfType<Markdig.Syntax.FencedCodeBlock>().FirstOrDefault();
    Check(codeBlock != null && codeBlock.Info == "csharp" && codeBlock.Lines.ToString().Contains("int x = 42;"), "Markdown : Bloc de code fenced avec langage et contenu");
    Check(mdDoc.OfType<Markdig.Extensions.Tables.Table>().Any(), "Markdown : Tableau en pipe table parsé");
    var unclosedStreamDoc = MarkdownPipelineHelper.Parse("```python\nprint('hello')");
    Check(unclosedStreamDoc.OfType<Markdig.Syntax.FencedCodeBlock>().Any(), "Markdown : Résilience face à un flux streaming non clôturé");
    Check(MarkdownPipelineHelper.Parse("").Count == 0 && MarkdownPipelineHelper.Parse(null).Count == 0, "Markdown : Chaîne vide ou nulle gérée gracieusement");

    var speedTracker = new GenerationSpeedTracker();
    speedTracker.AddSample(0.1, 2);
    speedTracker.AddSample(0.5, 20); // 18 tokens in 0.4s => 45 tok/s
    speedTracker.AddSample(1.0, 30); // 10 tokens in 0.5s => 20 tok/s
    speedTracker.Complete(1.2, 36);  // 36 tokens in 1.2s => 30 tok/s
    Check(speedTracker.MinSpeed.HasValue && speedTracker.MaxSpeed.HasValue, "SpeedTracker : Minimum et Maximum calculés");
    Check(speedTracker.MinSpeed!.Value <= speedTracker.AverageSpeed && speedTracker.AverageSpeed <= speedTracker.MaxSpeed!.Value, "SpeedTracker : Inégalité Min <= Moyenne <= Max respectée");
    Check(Math.Abs(speedTracker.AverageSpeed - 30.0) < 0.01, "SpeedTracker : Moyenne exacte");

    var chatSpeedStats = SpeedStats.Compute(new[] { (300, 10.0), (600, 10.0) });
    Check(chatSpeedStats.HasValue && chatSpeedStats.Value.Min == 30.0 && chatSpeedStats.Value.Max == 60.0 && Math.Abs(chatSpeedStats.Value.Avg - 45.0) < 0.01, "SpeedStats : Min, Max et Moyenne sur historique");
    Check(SpeedStats.Compute(Array.Empty<(int, double)>()) == null, "SpeedStats : Résultat null sur historique vide");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    // Git marks objects read-only. This directory is the unique test directory created above.
    foreach (var file in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
    Directory.Delete(workspace, true);
}
Console.WriteLine($"\n{passed} contrôles réussis.");

sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
}

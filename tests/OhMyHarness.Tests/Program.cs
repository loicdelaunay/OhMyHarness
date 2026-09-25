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
if(args.Contains("--browser-smoke")) { await ChromiumChecks.Run(Check); return; }
if(args.Contains("--chrome-smoke")) { await ChromeMcpChecks.Run(Check); return; }
await BrowserSkillChecks.Run(Check);
await WebHttpChecks.Run(Check);
if (args.Contains("--web-http")) { Console.WriteLine($"{passed} checks passed."); return; }
await CompleteDesignChecks.Run(Check);
if (args.Contains("--complete-design")) { Console.WriteLine($"{passed} checks passed."); return; }
await ResponseStyleChecks.Run(Check);
if (args.Contains("--response-styles")) { Console.WriteLine($"{passed} checks passed."); return; }
await UpdateChecks.Run(Check);
if (args.Contains("--updates-only")) { Console.WriteLine($"{passed} contrôles réussis."); return; }
await ConversationEnhancementChecks.Run(Check);
if (args.Contains("--conversation-enhancements")) { Console.WriteLine($"{passed} contrôles réussis."); return; }
await MemoryChecks.Run(Check);
if(args.Contains("--memory-only")) return;
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
var handoff = AgentHandoff.Create(12, "[Exploration] Plan: no edits.\n[Validation] Ready.");
Check(handoff.Role == "assistant" && ChatEngine.ToWire(handoff)["role"]!.GetValue<string>() == "user", "Rapport visible dans le chat mais transmis comme entrée à l’orchestrateur");
Check(ChatEngine.ToWire(handoff)["content"]!.GetValue<string>().Contains("Continue the original user task") && ChatEngine.ToWire(handoff)["content"]!.GetValue<string>().Contains("remain read-only"), "Reprise du parent explicite avec respect de son mode Plan ou Exécution");
var legacyHandoff = new Message { Role = "assistant", Content = AgentHandoff.ReportPrefix + "Ancien rapport" };
Check(ChatEngine.ToWire(legacyHandoff)["role"]!.GetValue<string>() == "user" && legacyHandoff.WireJson.Length == 0, "Ancien rapport réutilisable sans modifier la base");
var realAssistant = new Message { Role = "assistant", Content = "Réponse du modèle", WireJson = "{\"role\":\"assistant\",\"content\":\"Réponse\",\"reasoning_content\":\"reasoning\"}" };
Check(ChatEngine.ToWire(realAssistant)["reasoning_content"]!.GetValue<string>() == "reasoning", "Les vraies réponses conservent leur raisonnement fournisseur");
Check(MouseInput.NormalizeButton(null) == "left" && MouseInput.NormalizeButton("RIGHT") == "right", "Clics souris gauche et droit normalisés");
await Throws<ArgumentException>(() => Task.FromResult(MouseInput.NormalizeButton("middle")), "Bouton souris non autorisé refusé");
Check(MouseInput.NormalizeClickCount(1) == 1 && MouseInput.NormalizeClickCount(2) == 2, "Simple et double clic souris acceptés");
await Throws<ArgumentOutOfRangeException>(() => Task.FromResult(MouseInput.NormalizeClickCount(3)), "Nombre de clics souris invalide refusé");
Check(MouseInput.ToWindowsWheelDelta(240) == -240 && MouseInput.ToWindowsWheelDelta(-120) == 120, "Sens de défilement souris cohérent entre navigateur et Windows");
var directSlide = MouseInput.SlidePath(10, 20, 210, 120, "direct", new Random(1));
Check(directSlide.Count >= 8 && directSlide[^1].X == 210 && directSlide[^1].Y == 120 &&
    directSlide.All(step => Math.Abs((step.Y - 20) - (step.X - 10) * .5) < .0001), "Glissement direct rectiligne jusqu’à la destination");
var humanSlide = MouseInput.SlidePath(10, 20, 210, 120, "human", new Random(1));
Check(humanSlide[^1].X == 210 && humanSlide[^1].Y == 120 &&
    humanSlide.Any(step => Math.Abs((step.Y - 20) - (step.X - 10) * .5) > .1) &&
    humanSlide.Select(step => step.DelayMs).Distinct().Count() > 1, "Glissement humain avec imperfections et délais variables");
await Throws<ArgumentException>(() => Task.FromResult(MouseInput.SlidePath(0, 0, 10, 10, "unknown")), "Pattern de glissement invalide refusé");
await Throws<ArgumentException>(() => Task.FromResult(MouseInput.SlidePath(0, 0, double.NaN, 10, "direct")), "Coordonnées non finies refusées");
Check(PermissionModes.AutomaticDecision("deny") == false && PermissionModes.AutomaticDecision("allow") == true && PermissionModes.AutomaticDecision("ask") == null, "Politique globale des autorisations appliquée avant les dialogues");
Check(PermissionModes.Normalize("inconnu") == PermissionModes.Ask, "Politique d’autorisation invalide ramenée au mode Demander");
Check(HarnessDb.DatabasePath == Path.Combine(Path.GetDirectoryName(Environment.ProcessPath!)!, "database.sqlite"), "Base SQLite par défaut placée à côté du processus exécutable");
await PortableStorageChecks.Run(Check);
ConversationExportChecks.Run(Check);
var saveChord = KeyboardInput.ParseChord("ctrl+s");
Check(saveChord.Modifiers.SequenceEqual(["CTRL"]) && saveChord.Key == "S", "Raccourci clavier CTRL+S normalisé");
var aliasChord = KeyboardInput.ParseChord("control+return");
Check(aliasChord.Modifiers.SequenceEqual(["CTRL"]) && aliasChord.Key == "ENTER", "Alias clavier normalisés");
Check(KeyboardInput.ParseChord("ALT+TAB").Key == "TAB" && KeyboardInput.ParseChord("WIN+D").Key == "D" && KeyboardInput.ParseChord("F12").Key == "F12", "Touches spéciales clavier acceptées");
Check(new[] { "ALT", "CTRL", "SHIFT", "WIN" }.All(key => KeyboardInput.ParseChord(key) is { Modifiers.Count: 0 } chord && chord.Key == key), "Appui isolé sur Alt, Ctrl, Shift et Win accepté");
Check(KeyboardInput.ParseChord("Entrée").Key == "ENTER" && KeyboardInput.ParseChord(" windows ").Key == "WIN", "Alias français et modificateurs seuls normalisés");
Check(KeyboardInput.ParseChord("Command+Option+S").Modifiers.SequenceEqual(["WIN", "ALT"]), "Raccourci macOS Command/Option normalisé");
Check(!KeyboardInput.KeysForPlatform(true).Contains("PRINTSCREEN") && !KeyboardInput.KeysForPlatform(true).Contains("F24") && KeyboardInput.KeysForPlatform(true).Contains("F20"), "Catalogue macOS limité aux touches natives prises en charge");
var keyboardCatalog = JsonNode.Parse(KeyboardInput.DescribeKeys())!;
Check(keyboardCatalog["keys"]!.AsArray().All(key => KeyboardInput.ParseChord(key!.GetValue<string>()).Key == key.GetValue<string>()), "Toutes les touches du catalogue sont exécutables par le parseur");
Check(keyboardCatalog["examples"]!.AsArray().All(example => KeyboardInput.ParseChord(example!.GetValue<string>()) != null), "Exemples de keyboard_keys valides");
await Throws<ArgumentException>(() => Task.FromResult(KeyboardInput.ParseChord("CTRL+")), "Raccourci incomplet refusé");
await Throws<ArgumentException>(() => Task.FromResult(KeyboardInput.ParseChord("CTRL+CTRL+S")), "Modificateur répété refusé");
await Throws<ArgumentException>(() => Task.FromResult(KeyboardInput.ParseChord("CTRL+S+Q")), "Raccourci avec plusieurs touches principales refusé");
await Throws<ArgumentException>(() => Task.FromResult(KeyboardInput.ParseChord("")), "Raccourci clavier vide refusé");
var contextSample = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(
    new JsonObject { ["type"] = "text", ["text"] = new string('a', 400) },
    new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + new string('A', 20_000) } }) });
Check(ContextWindow.Estimate(contextSample) is >= 100 and < 5000 && ContextWindow.ShouldCompact(950, 1000) && !ContextWindow.ShouldCompact(949, 1000), "Estimation du contexte et seuil de compaction à 95 %");
var imageContextMessage = new Message { Content = "Image context estimate", Attachments = [new Attachment { Mime = "image/png", Data = new byte[1024 * 1024] }] };
var encodedEstimate = ContextWindow.Estimate(ChatEngine.ToWire(imageContextMessage));
Check(ContextDetails.From([imageContextMessage], 128000).Used == encodedEstimate, "Estimation du contexte image identique sans encodage base64");
imageContextMessage.Attachments[0].Data = [];
Check(ContextDetails.From([imageContextMessage], 128000).Used == encodedEstimate, "Métadonnées des images hors écran suffisantes pour le compteur de contexte");
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
Check(ChatEngine.Endpoint("http://example.com/v1", "models").AbsoluteUri == "http://example.com/v1/models", "Connexion fournisseur HTTP distante autorisée explicitement");
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
var policyReads = 0;
JsonObject? openCodePolicyPayload = null;
using (var client = new HttpClient(new FakeHandler(async request =>
{
    var route = request.RequestUri!.AbsolutePath;
    if (route.EndsWith("/prompt_async")) { openCodePolicyPayload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject(); return new(System.Net.HttpStatusCode.NoContent); }
    if (route.EndsWith("/permission")) return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") };
    if (route.EndsWith("/message")) return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(++policyReads % 2 == 1 ? "[]" : "[{\"info\":{\"id\":\"answer\",\"role\":\"assistant\",\"time\":{\"completed\":1}},\"parts\":[{\"type\":\"text\",\"text\":\"Plan\"}]}]") };
    throw new Exception(route);
})))
{
    var native = new Provider { Kind = "opencode", BaseUrl = "http://127.0.0.1:4096", Model = "test/coder", OpenCodeTools = true };
    var engine = new OpenCodeEngine(client);
    await engine.PromptAsync(native, "", "C:\\test", "session", "Plan", "system", [], _ => { }, default, policy: new("plan", "forced"));
    Check(openCodePolicyPayload?["tools"]?["*"]?.GetValue<bool>() == false && openCodePolicyPayload?["tools"]?["read"]?.GetValue<bool>() == true
        && openCodePolicyPayload?["tools"]?["task"] == null, "OpenCode Plan : refus global et seules exceptions de lecture");
    await engine.PromptAsync(native, "", "C:\\test", "session", "Execute", "system", [], _ => { }, default, policy: new("execute", "disabled"));
    Check(openCodePolicyPayload?["tools"]?["task"]?.GetValue<bool>() == false, "OpenCode Disable interdit task");
    await engine.PromptAsync(native, "", "C:\\test", "session", "Execute", "system", [], _ => { }, default, policy: new("execute", "auto"));
    Check(openCodePolicyPayload?["tools"]?["task"]?.GetValue<bool>() == true && openCodePolicyPayload?["tools"]?["*"] == null, "OpenCode Exécution Auto rétablit les outils sans refus Plan résiduel");
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
    await File.WriteAllTextAsync(Path.Combine(sources, "lines.txt"), "première\r\n\r\ntroisième\nquatrième");
    Check(await access.ReadAsync("lines.txt", default, 2, 3) == "lines.txt — lignes 2 à 3 (incluses)\n2: \n3: troisième\n", "Lecture partielle inclusive, lignes vides et CRLF");
    Check((await access.ReadAsync("lines.txt", default, 4, 8)).EndsWith("4: quatrième\n"), "Lecture partielle s'arrête en fin de fichier sans saut final");
    Check((await access.ReadAsync("lines.txt", default, 9, 10)).Contains("hors fichier"), "Début hors fichier signalé");
    await Throws<ArgumentException>(() => access.ReadAsync("lines.txt", default, 0, 2), "Numérotation commence à 1");
    await Throws<ArgumentException>(() => access.ReadAsync("lines.txt", default, 3, 2), "Bornes inversées refusées");
    await Throws<ArgumentException>(() => access.ReadAsync("lines.txt", default, 1), "Bornes partielles manquantes refusées");
    await Throws<ArgumentException>(() => access.ReadAsync("lines.txt", default, 1, 2001), "Plage trop longue refusée");
    await File.WriteAllTextAsync(Path.Combine(sources, "large.txt"), string.Concat(Enumerable.Repeat("Une ligne de texte\n", 10000)));
    Check((await access.ReadAsync("large.txt", default, 9999, 10000)).Contains("10000: Une ligne de texte"), "Extrait possible dans un fichier supérieur à 128 Ko");
    await Throws<InvalidOperationException>(() => access.ReadAsync("large.txt", default), "Limite de lecture complète conservée");
    await Throws<UnauthorizedAccessException>(() => access.ReadAsync("../outside.cs", default, 1, 2), "Lecture partielle ne contourne pas le périmètre");
    var readSchema = ChatEngine.ToolDefinitions(true, false).First(x => x?["function"]?["name"]?.GetValue<string>() == "read_source")!["function"]!["parameters"]!;
    Check(readSchema["properties"]!["start_line"]!["type"]!.GetValue<string>() == "integer" && readSchema["required"]!.AsArray().Count == 1, "Schéma read_source : bornes entières optionnelles");
    Check(!access.List().Contains(".env"), "Secrets exclus de la liste");
    await access.WriteAsync("sub/b.cs", "class B {}", default);
    Check((await access.ReadAsync("sub/b.cs", default)) == "class B {}", "Écriture de fichier source autorisée");
    await access.ModifyAsync("sub/b.cs", "class B", "class BModified", default);
    Check((await access.ReadAsync("sub/b.cs", default)) == "class BModified {}", "Modification chirurgicale de fichier source autorisée");
    await Throws<InvalidOperationException>(() => access.ModifyAsync("sub/b.cs", "nonexistent", "foo", default), "Modification échoue si texte cible absent");
    await access.WriteAsync("scripts/data/card_data.gd", "@export var lore: String = \"\"\r\n", default);
    await access.ModifyAsync("scripts/data/card_data.gd", "lore: String", "lore: StringName", default);
    Check(await access.ReadAsync("scripts/data/card_data.gd", default) == "@export var lore: StringName = \"\"\r\n", "edit_source accepte Godot .gd et préserve CRLF");
    await access.WriteAsync("LICENSE", "original", default);
    await access.ModifyAsync("LICENSE", "original", "updated", default);
    Check(await access.ReadAsync("LICENSE", default) == "updated", "edit_source accepte un fichier sans extension");
    await access.WriteAsync("data.custom", "before", default);
    await access.ModifyAsync("data.custom", "before", "after", default);
    Check(await access.ReadAsync("data.custom", default) == "after", "edit_source accepte une extension arbitraire");
    var utf16Path = Path.Combine(sources, "unicode.gd");
    await File.WriteAllTextAsync(utf16Path, "été\r\n", new System.Text.UnicodeEncoding(false, true, true));
    await access.ModifyAsync("unicode.gd", "été", "hiver", default);
    var utf16Bytes = await File.ReadAllBytesAsync(utf16Path);
    Check(utf16Bytes.AsSpan().StartsWith(new byte[] {255, 254}) && new System.Text.UnicodeEncoding(false, true, true).GetString(utf16Bytes, 2, utf16Bytes.Length - 2) == "hiver\r\n", "edit_source préserve BOM et encodage UTF-16");
    await access.WriteAsync("unicode.gd", "print(\"été\")\r\n", default);
    utf16Bytes = await File.ReadAllBytesAsync(utf16Path);
    Check(utf16Bytes.AsSpan().StartsWith(new byte[] {255, 254}) && new System.Text.UnicodeEncoding(false, true, true).GetString(utf16Bytes, 2, utf16Bytes.Length - 2) == "print(\"été\")\r\n", "write_source préserve l'encodage d'un fichier texte existant");
    var binaryPath = Path.Combine(sources, "binary.gd");
    var binaryBytes = new byte[] { 0, 1, 2, 255, 10 };
    await File.WriteAllBytesAsync(binaryPath, binaryBytes);
    await Throws<InvalidOperationException>(() => access.ModifyAsync("binary.gd", "a", "b", default), "edit_source refuse un binaire même avec extension texte");
    await Throws<InvalidOperationException>(() => access.WriteAsync("binary.gd", "bad", default), "write_source refuse d'écraser un binaire");
    Check((await File.ReadAllBytesAsync(binaryPath)).SequenceEqual(binaryBytes), "edit_source laisse le binaire inchangé");
    await Throws<ArgumentException>(() => access.ModifyAsync("LICENSE", "", "bad", default), "edit_source refuse une cible vide");
    await Throws<UnauthorizedAccessException>(() => access.ModifyAsync(".env", "secret", "bad", default), "edit_source conserve l'exclusion des secrets");
    await Throws<UnauthorizedAccessException>(() => access.ModifyAsync("../outside.cs", "outside", "bad", default), "edit_source conserve le périmètre des sources");
    await Throws<UnauthorizedAccessException>(() => access.ReadAsync("../outside.cs", default), "Traversée de répertoire bloquée");
    await Throws<UnauthorizedAccessException>(() => access.ReadAsync(".env", default), "Lecture .env bloquée");

    var proj = new Project();
    Check(proj.GetSourceFolders().Count == 0, "Dossiers sources initialement vides");
    proj.SetSourceFolders(["  C:\\Projects\\Front  ", "C:\\Projects\\Back", "c:\\projects\\front"]);
    Check(proj.GetSourceFolders().Count == (OperatingSystem.IsWindows() ? 2 : 3), "SetSourceFolders déduplique selon la sensibilité de casse du système");
    proj.SourceFolder = "C:\\A;C:\\B\nD:\\C";
    Check(proj.GetSourceFolders().Count == 3 && proj.GetSourceFolders()[2] == "D:\\C", "GetSourceFolders gère séparateurs multiples");

    var front = Path.Combine(workspace, "frontend"); Directory.CreateDirectory(front);
    var shellResult = await WorkspaceTools.ShellAsync(OperatingSystem.IsWindows() ? "Write-Output 'portable-shell-ok'" : "printf portable-shell-ok", workspace, default);
    Check(shellResult.Contains("portable-shell-ok") && shellResult.Contains("Exit code: 0"), "Terminal adapté au système Windows/macOS");
    if (!OperatingSystem.IsWindows())
    {
        var upper = Path.Combine(workspace, "CaseRoot"); var lower = Path.Combine(workspace, "caseroot");
        Directory.CreateDirectory(upper);
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(new SourceAccess(upper).Resolve(Path.Combine(lower, "escape.txt"))), "Chemins sensibles à la casse : aucune évasion vers un dossier homonyme");
    }
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
    var parallelPath = Path.Combine(workspace, "parallel.sqlite");
    await using (var setup = new HarnessDb(parallelPath))
    {
        await setup.InitializeAsync();
        var projectA = new Project { Name = "A", SourceFolder = front, Chats = [new Chat { Title = "A" }] };
        var projectB = new Project { Name = "B", SourceFolder = back, Chats = [new Chat { Title = "B" }] };
        setup.Projects.AddRange(projectA, projectB); await setup.SaveChangesAsync();
        var settings = new AppState { Language = "fr", ThinkingLevel = "high", EnabledSkills = "sources" };
        var selected = new Provider { BaseUrl = "https://example.com/v1", Model = "model-A", ProtectedKey = [1, 2] };
        var attached = new Attachment { Name = "a.png", Data = [1, 2, 3] };
        using var runA = new ConversationSession(projectA.Chats[0], projectA, selected, settings, "Question A", [attached], parallelPath);
        selected.Model = "model-B";
        using var runB = new ConversationSession(projectB.Chats[0], projectB, selected, settings, "Question B", [], parallelPath);
        selected.Model = "model-C"; selected.ProtectedKey[0] = 9; settings.ThinkingLevel = "low";
        projectA.SourceFolder = back; attached.Data[0] = 9;
        Check(runA.Provider.Model == "model-A" && runB.Provider.Model == "model-B" && runA.Provider.ProtectedKey[0] == 1,
            "Modèle et clé capturés indépendamment des changements du fournisseur actif");
        Check(runA.Project.SourceFolder == front && runA.Options.ThinkingLevel == "high" && runA.Images[0].Data[0] == 1,
            "Sources, thinking et images isolés des modifications pendant la génération");

        var continueA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var parallelClient = new HttpClient(new FakeHandler(async request =>
        {
            var payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            var name = payload["model"]!.GetValue<string>() == "model-A" ? "A" : "B";
            var prefix = Event(new { choices = new[] { new { delta = new { content = name + " partiel" } } } });
            var suffix = Event(new { choices = new[] { new { delta = new { content = " terminé" } } } }) + "data: [DONE]\n\n";
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StreamContent(new PausingStream(prefix, suffix, name == "A" ? continueA.Task : continueB.Task))
            };
        }));
        var parallelEngine = new ChatEngine(parallelClient);
        async Task Generate(ConversationSession run, TaskCompletionSource firstDelta)
        {
            var answer = new Message { ChatId = run.Chat.Id, Role = "assistant", State = "interrupted" };
            run.Db.Messages.Add(answer); await run.Db.SaveChangesAsync();
            try
            {
                var completion = await parallelEngine.StreamAsync(run.Provider, "test-key",
                    new JsonArray(new JsonObject { ["role"] = "user", ["content"] = run.Prompt }), [],
                    update => { answer.Content = update.Text; firstDelta.TrySetResult(); }, run.Cancellation.Token);
                answer.Content = completion.Message["content"]!.GetValue<string>(); answer.State = "complete";
            }
            finally { await run.Db.SaveChangesAsync(); }
        }
        var taskA = Generate(runA, firstA);
        var taskB = Generate(runB, firstB);
        await Task.WhenAll(firstA.Task, firstB.Task).WaitAsync(TimeSpan.FromSeconds(10));
        Check(!taskA.IsCompleted && !taskB.IsCompleted, "Deux conversations reçoivent leur flux simultanément");
        runA.Cancellation.Cancel();
        await Throws<OperationCanceledException>(() => taskA, "Arrêt ciblé de la première conversation");
        Check(!runB.Cancellation.IsCancellationRequested && !taskB.IsCompleted, "L’arrêt du premier chat ne coupe pas le second");
        continueB.SetResult(); await taskB.WaitAsync(TimeSpan.FromSeconds(10));
        await using var restored = new HarnessDb(parallelPath);
        var savedA = await restored.Messages.SingleAsync(x => x.ChatId == runA.Chat.Id);
        var savedB = await restored.Messages.SingleAsync(x => x.ChatId == runB.Chat.Id);
        Check(savedA.Content == "A partiel" && savedA.State == "interrupted" && savedB.Content == "B partiel terminé" && savedB.State == "complete",
            "Historiques distincts restaurés : réponse partielle A et réponse terminée B");
    }
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
        Check((await db.Database.GetAppliedMigrationsAsync()).SequenceEqual(db.Database.GetMigrations()), "Migrations et démarrage idempotent");
        Check(!(await db.States.SingleAsync()).AutoContinue && !await db.McpServers.AnyAsync(), "Auto-continuation désactivée et liste MCP vide après migration");
        var template = await db.Templates.SingleAsync();
        Check(template.Name == "Web app" && template.Content.Contains("index.html"), "Template Web app initial créé par migration");
        template.Content = "Mon template personnalisé";
        var settings = await db.States.SingleAsync();
        settings.Language = "en"; settings.EnabledSkills = "review,planning"; settings.ThinkingLevel = "high"; settings.PermissionMode = PermissionModes.Allow;
        Check(settings.ShowReasoningDetails, "Détails du raisonnement visibles par défaut après migration");
        settings.ShowReasoningDetails = false;
        settings.AutoContinue = true;
        db.McpServers.Add(new McpServer { Name = "Test MCP", Transport = "http", Url = "https://example.com/mcp", Enabled = true });
        db.PermissionGrants.Add(new PermissionGrant { Scope = "browser-origin|https://example.com", Name = "Test", Details = "example.com" });
        Check(await db.Providers.CountAsync() == 2, "Deux fournisseurs initialisés sans doublons");
        var storedProvider = new Provider { Name = "Compatible local", BaseUrl = "http://localhost:11434/v1", Model = "custom-model", ContextLimit = 32768, SupportsImages = false };
        db.Providers.Add(storedProvider); await db.SaveChangesAsync(); settings.ProviderId = storedProvider.Id;
        var chat = await db.Chats.FirstAsync();
        Check(chat.ExecutionMode == "execute" && chat.OrchestrationMode == "disabled", "Modes de conversation par défaut après migration");
        chat.ExecutionMode = "plan"; chat.OrchestrationMode = "forced";
        db.Messages.Add(new Message { ChatId = chat.Id, Content = "image", Attachments = [new Attachment { Data = [1, 2, 3], Name = "test.png" }] });
        await db.SaveChangesAsync();
    }
    await using (var db = new HarnessDb(path))
    {
        var settings = await db.States.SingleAsync();
        var savedModeChat = await db.Chats.FirstAsync();
        Check(savedModeChat.ExecutionMode == "plan" && savedModeChat.OrchestrationMode == "forced", "Modes Plan et Forced restaurés depuis SQLite");
        Check(!settings.ShowReasoningDetails, "Préférence de raisonnement replié restaurée après réouverture");
        Check(settings.AutoContinue && (await db.McpServers.SingleAsync()).Url == "https://example.com/mcp", "Auto-continuation et serveur MCP restaurés après réouverture");
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
    if (OperatingSystem.IsLinux())
    {
        var protectedKey = KeyVault.Encrypt("fedora-test-key");
        var keyPath = Path.Combine(PortableStorage.Root, ".linux-key");
        Check(KeyVault.Decrypt(protectedKey) == "fedora-test-key" &&
              !System.Text.Encoding.UTF8.GetString(protectedKey).Contains("fedora-test-key") &&
              File.Exists(keyPath) &&
              (File.GetUnixFileMode(keyPath) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) == 0,
            "Linux provider key is encrypted with an owner-only installation key");
        var corrupted = (byte[])protectedKey.Clone(); corrupted[^4] = (byte)(corrupted[^4] == 'A' ? 'B' : 'A');
        await Throws<Exception>(() => Task.FromResult(KeyVault.Decrypt(corrupted)), "Tampered Linux provider key is rejected");
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
        var unbornFiles = await GitWorkspace.ListAsync([workspace], default);
        var unborn = unbornFiles.Single(x => x.Path == "git-test.txt");
        Check((await GitWorkspace.DiffAsync(unborn, default)).Contains("+"), "Git : fichier ajouté avant le premier commit");
        await WorkspaceTools.GitAsync(workspace, ["-c", "user.name=Tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "Fixture", "--quiet"], default);
        await File.WriteAllTextAsync(Path.Combine(workspace, "git-test.txt"), "staged-change\n");
        await WorkspaceTools.GitAsync(workspace, ["add", "git-test.txt"], default);
        await File.WriteAllTextAsync(Path.Combine(workspace, "git-test.txt"), "working-tree-change\n");
        var changedFiles = await GitWorkspace.ListAsync([workspace], default);
        var totalDiff = await GitWorkspace.DiffAsync(changedFiles.Single(x => x.Path == "git-test.txt"), default);
        Check(totalDiff.Contains("+working-tree-change") && !totalDiff.Contains("+staged-change"), "Git : diff total index et travail par rapport à HEAD");
        var sideBySide = GitWorkspace.ParseDiff(totalDiff);
        Check(sideBySide.Rows.Any(x => x.AfterLine == 1 && x.After == "working-tree-change" && x.Kind == "added") && sideBySide.Rows.Any(x => x.BeforeLine == 1 && x.Kind == "removed"), "Git : aperçu avant/après avec numéros de lignes");
        var stats = (await GitWorkspace.ListWithStatsAsync([workspace], default)).Single(x => x.Path == "git-test.txt");
        Check(stats.Added == 1 && stats.Removed == 1, "Git : compteurs des lignes ajoutées et supprimées");
        Check(GitWorkspace.ParseDiff("Binary files a/image.png and b/image.png differ").Notice != null, "Git : fichier binaire signalé sans fausses lignes");
        var hunks = GitWorkspace.ParseDiff("@@ -2,1 +2,1 @@\n-old\n+new\n@@ -20,1 +30,1 @@\n unchanged\n");
        Check(hunks.Rows.Last().BeforeLine == 20 && hunks.Rows.Last().AfterLine == 30, "Git : numérotation indépendante entre blocs de modifications");
        await File.WriteAllTextAsync(Path.Combine(workspace, "nouveau fichier é.txt"), "nouveau\n");
        var untracked = (await GitWorkspace.ListAsync([workspace], default)).Single(x => x.Path == "nouveau fichier é.txt");
        Check(untracked.Status == "??" && (await GitWorkspace.DiffAsync(untracked, default)).Contains("+nouveau"), "Git : fichier non suivi avec espaces et Unicode");
        await WorkspaceTools.GitAsync(workspace, ["add", "git-test.txt"], default);
        await WorkspaceTools.GitAsync(workspace, ["-c", "user.name=Tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "Rename baseline", "--quiet"], default);
        await WorkspaceTools.GitAsync(workspace, ["mv", "git-test.txt", "renamed.txt"], default);
        await File.AppendAllTextAsync(Path.Combine(workspace, "renamed.txt"), "extra-line\n");
        var renamed = (await GitWorkspace.ListAsync([workspace], default)).Single(x => x.Path == "renamed.txt");
        Check(renamed.PreviousPath == "git-test.txt" && (await GitWorkspace.DiffAsync(renamed, default)).Contains("working-tree-change"), "Git : renommage et modification depuis HEAD");
        var changes = await WorkspaceTools.GitChangesAsync(workspace, default);
        Check(changes.Contains("MODIFIED FILES") && changes.Contains("CHANGED LINES (STAGED)") && changes.Contains("+extra-line"), "Outil Git limité aux modifications et lignes changées");
        Check(LocalPreview.ResolveResource(sources, "a.cs") == Path.Combine(sources, "a.cs"), "Ressource locale autorisée dans le dossier approuvé");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ResolveResource(sources, "%2e%2e/outside.cs")), "Évasion URL encodée bloquée dans l’aperçu");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ResolveResource(sources, ".env")), "Fichiers secrets bloqués dans l’aperçu");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ValidatePath(@"\\server\share\file.html")), "Chemin réseau refusé pour aperçu local");
        await Throws<UnauthorizedAccessException>(() => Task.FromResult(LocalPreview.ValidatePath(Path.Combine(sources, "a.cs:secret"))), "Flux alternatif NTFS refusé");
    }
    var known = new Provider { Name="DeepSeek", Model="configured", DetectedModelsJson="[\"configured\",\"discovered\"]" };
    Check(ModelCatalog.GetModelsForProvider(known).SequenceEqual(new[]{"configured","discovered"}),"Catalogue constitué des modèles configurés et détectés uniquement");
    Check(ModelCatalog.GetModelsForProvider(null).Count==0,"Aucun catalogue supposé sans fournisseur");
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

    var featureRoot = Path.Combine(workspace, "source-tools");
    Directory.CreateDirectory(Path.Combine(featureRoot, "nested"));
    Directory.CreateDirectory(Path.Combine(featureRoot, "node_modules"));
    await File.WriteAllTextAsync(Path.Combine(featureRoot, "one.cs"), "alpha\r\nbeta\r\n", new System.Text.UTF8Encoding(true));
    await File.WriteAllTextAsync(Path.Combine(featureRoot, "nested", "two.cs"), "alpha\nalpha\n");
    await File.WriteAllTextAsync(Path.Combine(featureRoot, "nested", "card.gd"), "alpha godot\n");
    await File.WriteAllTextAsync(Path.Combine(featureRoot, "node_modules", "hidden.cs"), "alpha");
    await File.WriteAllTextAsync(Path.Combine(featureRoot, ".env.local"), "alpha");
    var featureSource = new SourceAccess(featureRoot);
    var globResult = await featureSource.SearchAsync("**/*.cs", null, false, false, default);
    Check(globResult.Contains("one.cs") && globResult.Contains("nested/two.cs") && !globResult.Contains("hidden.cs"), "Glob récursif et exclusions");
    var grepResult = await featureSource.SearchAsync("**/*", "ALPHA", false, true, default);
    Check(grepResult.Contains("one.cs:1:") && grepResult.Contains("two.cs:2:") && grepResult.Contains("card.gd:1:") && !grepResult.Contains(".env"), "Grep toutes extensions texte, lignes, casse et secrets exclus");
    Check((await featureSource.SearchAsync("*.cs", "^beta$", true, false, default)).Contains("one.cs:2:"), "Grep regex et glob non récursif");
    await Throws<ArgumentException>(() => featureSource.SearchAsync("../*", null, false, false, default), "Glob hors périmètre refusé");
    var beforePatch = await File.ReadAllBytesAsync(Path.Combine(featureRoot, "one.cs"));
    var plan = await featureSource.PreparePatchAsync([new("one.cs", "beta", "gamma"), new("new.md", null, "report\n")], default);
    Check(plan.Diff.Contains("-beta") && plan.Diff.Contains("+gamma") && plan.Diff.Contains("+++ b/new.md") && !File.Exists(Path.Combine(featureRoot, "new.md")), "Diff multi-fichiers sans écriture");
    await featureSource.ApplyPatchAsync(plan, default);
    Check((await File.ReadAllBytesAsync(Path.Combine(featureRoot, "one.cs"))).Take(3).SequenceEqual(beforePatch.Take(3)) && (await File.ReadAllTextAsync(Path.Combine(featureRoot, "one.cs"))).Contains("gamma\r\n"), "Patch préserve BOM UTF-8 et CRLF");
    Check(await File.ReadAllTextAsync(Path.Combine(featureRoot, "new.md")) == "report\n", "Patch crée un fichier");
    var godotPlan = await featureSource.PreparePatchAsync([new("nested/card.gd", "alpha", "beta")], default);
    await featureSource.ApplyPatchAsync(godotPlan, default);
    Check(await File.ReadAllTextAsync(Path.Combine(featureRoot, "nested", "card.gd")) == "beta godot\n", "Patch accepte Godot .gd");
    await File.WriteAllBytesAsync(Path.Combine(featureRoot, "binary.custom"), [0, 1, 2]);
    await Throws<InvalidOperationException>(() => featureSource.PreparePatchAsync([new("binary.custom", "a", "b")], default), "Patch refuse un binaire quelle que soit son extension");
    await Throws<InvalidOperationException>(() => featureSource.PreparePatchAsync([new("one.cs", "gamma", "bad"), new("nested/two.cs", "alpha", "ambiguous")], default), "Patch entier refusé si remplacement ambigu");
    Check((await File.ReadAllTextAsync(Path.Combine(featureRoot, "one.cs"))).Contains("gamma"), "Échec de validation ne modifie aucun fichier");
    await Throws<UnauthorizedAccessException>(() => featureSource.PreparePatchAsync([new("../outside.md", null, "bad")], default), "Patch hors sources refusé");
    await Throws<InvalidOperationException>(() => featureSource.PreparePatchAsync([new("one.cs", null, "bad")], default), "Création ne remplace pas un fichier existant");
    var stalePlan = await featureSource.PreparePatchAsync([new("one.cs", "gamma", "delta")], default);
    await featureSource.WriteAsync("one.cs", "external change", default);
    await Throws<InvalidOperationException>(() => featureSource.ApplyPatchAsync(stalePlan, default), "Patch refuse un fichier modifié après aperçu");
    var defs = new JsonArray(); SourceTools.AddDefinitions(defs, true, "code_search,patch_sources");
    Check(defs.Count == 3 && SourceTools.CanRead("patch_sources"), "Définitions des nouveaux skills autonomes");
    var patchArgs = JsonNode.Parse("""{"edits":[{"path":"denied.md","old_text":null,"new_text":"test"}],"dry_run":false}""")!.AsObject();
    var deniedPatch = await SourceTools.ExecuteAsync(featureSource, "patch_sources", patchArgs, () => "patch_sources", (_, _, _) => Task.FromResult(false), default);
    Check(deniedPatch.Contains("refusé") && !File.Exists(Path.Combine(featureRoot, "denied.md")), "Autorisation refusée : aucun patch");
    var enabledPatch = "patch_sources";
    await Throws<UnauthorizedAccessException>(() => SourceTools.ExecuteAsync(featureSource, "patch_sources", patchArgs, () => enabledPatch, (_, _, _) => { enabledPatch = ""; return Task.FromResult(true); }, default), "Désactivation pendant autorisation honorée");
    string FileHtml(string markdown) => Markdig.Markdown.ToHtml(MarkdownPipelineHelper.Parse(markdown), MarkdownPipelineHelper.Pipeline);
    var reportHtml = FileHtml("Rapport écrit : docs/rapport-comparatif-opencode.html (autonome). `docs/a.md` [Rapport](docs/rapport.html)");
    Check(reportHtml.Split("omh-file:").Length == 4, "Liens locaux : texte, code inline et Markdown");
    Check(LocalFileLinks.PathFromUrl("omh-file:docs%2Frapport.html") == "docs/rapport.html", "Décodage du chemin local");
    Check(!FileHtml("https://example.com/report.html\n\n```\ndocs/no.html\n```").Contains("omh-file:"), "URLs et blocs de code non convertis en fichiers");
    Check(FileHtml("[Rapport](<docs/mon rapport.html>)").Contains("omh-file:"), "Lien explicite avec espaces");

    foreach (var blockedTool in new[] { "write_source", "edit_source", "patch_sources", "run_terminal", "browser_dom", "desktop_keyboard", "browse", "mcp_fake", "new_unknown_tool" })
        await Throws<UnauthorizedAccessException>(() => { AgentPolicy.Demand("plan", blockedTool); return Task.CompletedTask; }, "Plan interdit " + blockedTool);
    Check(AgentPolicy.Allowed("plan", "read_source") && AgentPolicy.Allowed("plan", "delegate_tasks") && AgentPolicy.Allowed("execute", "write_source"), "Plan autorise lecture et délégation contrôlée ; Exécution autorise édition");
    Check(!AgentPolicy.Allowed("plan", "browser_javascript") && AgentPolicy.Allowed("execute", "browser_javascript"), "JavaScript arbitraire interdit en Plan");
    await File.WriteAllTextAsync(Path.Combine(featureRoot, "AGENTS.md"), "ROOT-CONVENTION");
    await File.WriteAllTextAsync(Path.Combine(featureRoot, "nested", "AGENTS.md"), "NESTED-CONVENTION");
    await File.WriteAllTextAsync(Path.Combine(featureRoot, "node_modules", "AGENTS.md"), "IGNORED-CONVENTION");
    var instructions = await ProjectInstructions.LoadAsync([featureRoot], default);
    Check(instructions.Contains("ROOT-CONVENTION") && instructions.Contains("NESTED-CONVENTION") && !instructions.Contains("IGNORED-CONVENTION"), "AGENTS.md racine et sous-dossiers chargés avec exclusions");
    var skillFolder = Path.Combine(workspace, "custom-skills"); var customSkills = new CustomSkills(skillFolder);
    customSkills.EnsureTemplate();
    Check(customSkills.Discover().Single().Name == "exemple-revue", "Modèle SKILL.md créé et découvert");
    Check(customSkills.Catalog("custom:exemple-revue").Contains("exemple-revue") && !customSkills.Catalog("custom:exemple-revue").Contains("# Exemple"), "Catalogue de skills sans injection de tout le contenu");
    Check((await customSkills.ReadAsync("exemple-revue", null, "custom:exemple-revue", default)).Contains("# Exemple"), "Chargement du skill à la demande");
    Check((await customSkills.ReadAsync("exemple-revue", "resources/checklist.md", "custom:exemple-revue", default)).Contains("Checklist"), "Ressources relatives du skill lisibles");
    await Throws<UnauthorizedAccessException>(() => customSkills.ReadAsync("exemple-revue", "../outside.md", "custom:exemple-revue", default), "Ressource hors du skill refusée");
    await Throws<UnauthorizedAccessException>(() => customSkills.ReadAsync("exemple-revue", null, "", default), "Skill désactivé inaccessible");
    var originalTemplate = await File.ReadAllTextAsync(Path.Combine(skillFolder, "exemple-revue", "SKILL.md"));
    await File.AppendAllTextAsync(Path.Combine(skillFolder, "exemple-revue", "SKILL.md"), "\nUSER-CUSTOMIZATION"); customSkills.EnsureTemplate();
    Check((await File.ReadAllTextAsync(Path.Combine(skillFolder, "exemple-revue", "SKILL.md"))).Contains("USER-CUSTOMIZATION"), "Le modèle de skill ne remplace pas les personnalisations");
    var runtimeChat = new Chat { Id = 1234, ExecutionMode = "plan", OrchestrationMode = "forced" };
    var runtimeProject = new Project { SourceFolder = featureRoot };
    await using(var runtimeDb = new HarnessDb(Path.Combine(workspace,"runtime.sqlite")))
    {
        await runtimeDb.InitializeAsync(); runtimeDb.Projects.Add(runtimeProject);await runtimeDb.SaveChangesAsync();
        runtimeChat.ProjectId=runtimeProject.Id;runtimeDb.Chats.Add(runtimeChat);await runtimeDb.SaveChangesAsync();
    }
    using (var session = new ConversationSession(runtimeChat, runtimeProject, new Provider(), new AppState { EnabledSkills = "sources,write_sources,custom:exemple-revue" }, "Analyze", [], Path.Combine(workspace, "runtime.sqlite")))
    {
        runtimeChat.ExecutionMode = "execute";
        Check(session.Chat.ExecutionMode == "plan", "Le mode est figé pour la génération en cours");
        var childRequests = 0; var policyDenials = 0;
        var runtime = new AgentRuntime(session, customSkills, (wire, tools, ct) => {
            Interlocked.Increment(ref childRequests);
            Check(!tools.Any(x => x?["function"]?["name"]?.GetValue<string>() is "write_source" or "delegate_tasks"), "Sous-agent Plan sans écriture ni récursion");
            if (wire.Count == 2) return Task.FromResult(new Completion(new JsonObject { ["role"] = "assistant", ["tool_calls"] = new JsonArray(new JsonObject {
                ["id"] = "bad-child", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "write_source", ["arguments"] = "{\"path\":\"forbidden.md\",\"content\":\"bad\"}" } }) }, 1, 1, 1));
            if (wire.Last()?["content"]?.GetValue<string>().Contains("Mode Plan") == true) Interlocked.Increment(ref policyDenials);
            return Task.FromResult(new Completion(new JsonObject { ["role"] = "assistant", ["content"] = "Analysis complete" }, 1, 1, 1));
        }, (_, _, _) => throw new Exception("No permission dialog should be needed."), _ => Task.CompletedTask);
        Check((await runtime.InitializeAsync(default)).Contains("ROOT-CONVENTION"), "Instructions transmises à l’orchestrateur");
        var report = await runtime.ForcedAsync(default);
        Check(await session.Db.Subagents.CountAsync(x=>x.ChatId==runtimeChat.Id && x.Status=="completed")==2, "Sous-agents conservent leurs échanges et leur statut final");
        Check(childRequests == 4 && policyDenials == 2 && !File.Exists(Path.Combine(featureRoot, "forbidden.md")) && report.Contains("Exploration") && report.Contains("Validation"), "Forced lance deux sous-agents et bloque leurs écritures malgré un appel forgé");
    }

    var authoredRoot = Path.Combine(workspace, "authored-skills");
    var authored = new CustomSkills(authoredRoot, [featureRoot], runtimeProject.Id);
    var aliases = new SourceAccess([featureRoot]).Aliases;
    var projectAlias = aliases.Keys.Single();
    var activeSkills = new List<string>();
    Task EnableAuthored(string id, CancellationToken _) { activeSkills.Add(id); return Task.CompletedTask; }
    JsonObject Draft(string name, string scope, string? target = null) => new() {
        ["name"] = name, ["scope"] = scope, ["project_root"] = target,
        ["description"] = "Procédure réutilisable", ["instructions"] = "# Étapes\n1. Examiner le contexte.\n2. Vérifier le résultat."
    };
    var createdProject = await SkillAuthoring.CreateAsync(authored, [featureRoot], Draft("analyse-godot", "project", projectAlias),
        (_, _, _) => Task.FromResult(true), EnableAuthored, default);
    var projectId = $"project:{runtimeProject.Id}:{projectAlias}:analyse-godot";
    Check(createdProject.Contains(projectId) && activeSkills.Contains(projectId)
        && File.Exists(Path.Combine(featureRoot, ".omh-ai", "skills", "analyse-godot", "SKILL.md")),
        "Skill daté créé et activé dans .omh-ai/skills du projet");
    Check((await authored.ReadAsync(projectId, null, projectId, default)).Contains("created_utc:"),
        "Skill projet disponible par son identifiant et chargé à la demande");
    Check(!new CustomSkills(authoredRoot).Discover().Any(x => x.Id == projectId), "Skill projet isolé des autres projets");
    await Throws<UnauthorizedAccessException>(() => SkillAuthoring.CreateAsync(authored, [featureRoot], Draft("refuse", "global"),
        (_, _, _) => Task.FromResult(false), EnableAuthored, default), "Création refusée sans écriture");
    Check(!Directory.Exists(Path.Combine(authoredRoot, "refuse")), "Refus sans dossier créé");
    Check(!AgentPolicy.Allowed("plan", "create_skill") && AgentPolicy.Allowed("plan", "skill_locations"),
        "Plan interdit la création de skills et autorise la liste des emplacements");
    var removals = new List<string>();
    for (var i = 0; i < 20; i++)
        await SkillAuthoring.CreateAsync(authored, [featureRoot], Draft($"procedure-{i:00}", "global"),
            (_, detail, _) => { if (detail.Contains("Supprimer le plus ancien")) removals.Add(detail); return Task.FromResult(true); },
            EnableAuthored, default);
    Check(authored.Discover().Count(x => x.Id.StartsWith("custom:")) == 20
        && removals.Count == 1 && !Directory.Exists(Path.Combine(authoredRoot, "exemple-revue"))
        && !Directory.Exists(Path.Combine(authoredRoot, "refuse")),
        "Limite de 20 skills globaux et suppression du plus ancien après autorisation");
    Check(Directory.Exists(Path.Combine(authoredRoot, "procedure-19"))
        && authored.Discover().Any(x => x.Id == "custom:procedure-19"), "Le nouveau skill reste utilisable après rotation");
    using (var authorSession = new ConversationSession(runtimeChat, runtimeProject, new Provider(),
        new AppState { EnabledSkills = SkillAuthoring.SkillId }, "Build reusable skill", [], Path.Combine(workspace, "runtime.sqlite")))
    {
        var live = SkillAuthoring.SkillId;
        var authorRuntime = new AgentRuntime(authorSession, authored,
            (_, _, _) => throw new Exception("No model request expected."),
            (_, _, _) => Task.FromResult(true), _ => Task.CompletedTask,
            _ => Task.FromResult(live), enableSkill: (id, _) => { live += "," + id; return Task.CompletedTask; });
        var definitions = new JsonArray(); authorRuntime.AddDefinitions(definitions);
        Check(definitions.Any(x => x?["function"]?["name"]?.GetValue<string>() == "create_skill"),
            "Auto-création expose son outil uniquement quand le skill est actif");
        var locationList = await authorRuntime.CallAsync("skill_locations", new JsonObject(), default);
        Check(locationList.Contains(".omh-ai") && locationList.Contains(projectAlias), "L’agent voit les emplacements globaux et du projet");
        var result = await authorRuntime.CallAsync("create_skill", Draft("outil-runtime", "project", projectAlias), default);
        var createdId = $"project:{runtimeProject.Id}:{projectAlias}:outil-runtime";
        Check(result.Contains(createdId) && live.Contains(createdId)
            && (await authorRuntime.CallAsync("load_skill", new JsonObject { ["name"] = createdId }, default)).Contains("# Étapes"),
            "Le runtime crée, active et charge le nouveau skill de projet");
        live = "";
        await Throws<UnauthorizedAccessException>(() => authorRuntime.CallAsync("create_skill", Draft("bloque", "project", projectAlias), default),
            "Désactivation en cours de conversation refuse la création");
    }

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
await WorkflowChecks.Run(Check);
await SandboxChecks.Run(Check);
await TerminalChecks.Run(Check);
await FeatureChecks.Run(Check);
await InboxChecks.Run(Check);
await AppearanceMcpChecks.Run(Check);
await VisionChecks.Run(Check);
ApplicationChecks.Run(Check);
await WorkspaceEnhancementChecks.Run(Check);
await SchedulingChecks.Run(Check);
Console.WriteLine($"\n{passed} contrôles réussis.");

sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
}

sealed class PausingStream(string prefix, string suffix, Task resume) : Stream
{
    readonly byte[] first = System.Text.Encoding.UTF8.GetBytes(prefix);
    readonly byte[] last = System.Text.Encoding.UTF8.GetBytes(suffix);
    int offset;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => first.Length + last.Length;
    public override long Position { get => offset; set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (offset >= first.Length) await resume.WaitAsync(ct);
        var bytes = offset < first.Length ? first : last;
        var localOffset = offset < first.Length ? offset : offset - first.Length;
        var count = Math.Min(buffer.Length, bytes.Length - localOffset);
        bytes.AsMemory(localOffset, count).CopyTo(buffer); offset += count;
        return count;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

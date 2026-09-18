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
    var path = Path.Combine(workspace, "state.db");
    await using (var upgrade = new HarnessDb(Path.Combine(workspace, "upgrade.db")))
    {
        var first = upgrade.Database.GetMigrations().First();
        await upgrade.GetService<IMigrator>().MigrateAsync(first);
        await upgrade.Database.ExecuteSqlRawAsync("INSERT INTO States (Id, ProviderId, BrowserUrl) VALUES (1, 1, 'https://example.com')");
        await upgrade.Database.MigrateAsync();
        var migrated = await upgrade.States.SingleAsync();
        Check(migrated.Language == "fr" && migrated.EnabledSkills == "sources,web" && migrated.BrowserUrl == "https://example.com" && migrated.ThinkingLevel == "auto", "Mise à niveau d’une base existante sans perte d’état");
    }
    await using (var db = new HarnessDb(path))
    {
        await db.InitializeAsync(); await db.InitializeAsync();
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 3, "Migrations et démarrage idempotent");
        var settings = await db.States.SingleAsync();
        settings.Language = "en"; settings.EnabledSkills = "review,planning"; settings.ThinkingLevel = "high";
        Check(await db.Providers.CountAsync() == 2, "Deux fournisseurs initialisés sans doublons");
        var chat = await db.Chats.FirstAsync();
        db.Messages.Add(new Message { ChatId = chat.Id, Content = "image", Attachments = [new Attachment { Data = [1, 2, 3], Name = "test.png" }] });
        await db.SaveChangesAsync();
    }
    await using (var db = new HarnessDb(path))
    {
        var settings = await db.States.SingleAsync();
        Check(settings.Language == "en" && settings.EnabledSkills == "review,planning" && settings.ThinkingLevel == "high", "Langue, skills et thinking restaurés");
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
        var restored = await db.Messages.Include(x => x.Attachments).SingleAsync();
        Check(restored.Attachments.Single().Data.SequenceEqual(new byte[] { 1, 2, 3 }), "Historique et images restaurés après réouverture");
        var wire = ChatEngine.ToWire(restored);
        Check(wire["content"]![1]!["image_url"]!["url"]!.GetValue<string>().EndsWith("AQID"), "Image sérialisée en contenu multimodal");
        db.Projects.Remove(await db.Projects.SingleAsync()); await db.SaveChangesAsync();
        Check(await db.Messages.CountAsync() == 0 && await db.Set<Attachment>().CountAsync() == 0, "Suppression du projet en cascade");
    }
    if (OperatingSystem.IsWindows())
    {
        var encrypted = KeyVault.Encrypt("clé-test");
        Check(KeyVault.Decrypt(encrypted) == "clé-test" && !System.Text.Encoding.UTF8.GetString(encrypted).Contains("clé-test"), "Clé API chiffrée par DPAPI");
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
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(workspace, true); }
Console.WriteLine($"\n{passed} contrôles réussis.");

sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OhMyHarness.Core;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

static class ConversationEnhancementChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "omh-enhancements-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string previous = PortableStorage.Root;
        try
        {
            var source = Path.Combine(root, "sources"); Directory.CreateDirectory(source);
            for (int i = 0; i < 2105; i++) Directory.CreateDirectory(Path.Combine(source, $"folder-{i:0000}"));
            var nested = Path.Combine(source, "folder-2104", "one", "two"); Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(source, "AGENTS.md"), "Root convention");
            await File.WriteAllTextAsync(Path.Combine(nested, "agent.md"), "Nested convention");
            var instructions = await ProjectInstructions.LoadAsync([source], default);
            check(instructions.Contains("Root convention") && instructions.Contains("Nested convention"), "Instructions : plus de 2 000 dossiers et agent.md imbriqué sans interruption");
            await File.WriteAllTextAsync(Path.Combine(source, "folder-0000", "Agent.md"), new string('x', 33000));
            check((await ProjectInstructions.LoadAsync([source], default)).Contains("Nested convention"), "Instructions : fichier trop grand ignoré sans masquer les suivants");
            check(await ProjectInstructions.LoadAsync([Path.Combine(root, "absent")], default) == "", "Instructions : dossier absent ignoré");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); bool stopped = false;
                try { await ProjectInstructions.LoadAsync([source], cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
                check(stopped, "Instructions : annulation conservée");
            }
            var features = new FeatureSettings { VisionInstruction = "Read labels precisely", VisionComponents = true };
            var question = VisionBridge.BuildQuestion("Describe", features, "Focus START");
            check(question.Contains("Read labels precisely") && question.Contains("Focus START") && question.Contains("bounding box") && question.Contains("not claim these are absolute desktop"), "Vision : instructions et formes relatives à l’image transmises");
            check(!VisionBridge.BuildQuestion("Describe", features, mode: "describe").Contains("bounding box"), "Vision : mode descriptif peut remplacer le mode composants");

            string database = Path.Combine(root, "database.sqlite"); PortableStorage.UseDatabase(database);
            await using var db = new HarnessDb(database); await db.InitializeAsync();
            var project = new Project { Name = "Enhancements" }; db.Projects.Add(project); await db.SaveChangesAsync();
            var chat = new Chat { ProjectId = project.Id }; db.Chats.Add(chat); await db.SaveChangesAsync();
            db.Messages.Add(new Message { ChatId = chat.Id, Content = "A title for a tactical card game", Role = "user" }); await db.SaveChangesAsync();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20260923123741_PersistentMemory"); await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            chat = await db.Chats.SingleAsync(c => c.Id == chat.Id);
            check(!chat.IsFavorite && await db.Messages.AnyAsync(m => m.ChatId == chat.Id), "Favoris : migration conserve l’historique et initialise sans favori");
            chat.IsFavorite = true; await db.SaveChangesAsync();
            check(!db.Database.HasPendingModelChanges(), "Favoris : snapshot EF concorde avec le modèle");
            var provider = await db.Providers.FirstAsync();
            provider.BaseUrl = "https://naming.example/v1";
            var state = await db.States.SingleAsync();
            features.NamingProviderId = provider.Id; features.NamingModel = "naming-small"; state.FeaturesJson = features.Json(); await db.SaveChangesAsync();
            var handler = new NamingHandler(); using var http = new HttpClient(handler);
            var none = await ConversationNaming.RenameAsync(database, chat.Id, http, (_, _) => Task.FromResult("private-key"), true, default);
            check(none == null && handler.Calls == 0, "Nommage : désactivé par défaut, aucun appel API");
            features.AutoNameConversations = true; state.FeaturesJson = features.Json(); await db.SaveChangesAsync();
            var title = await ConversationNaming.RenameAsync(database, chat.Id, http, (_, _) => Task.FromResult("private-key"), true, default);
            check(title == "Jeu de cartes tactique" && handler.Payload?["model"]?.GetValue<string>() == "naming-small" && handler.Payload?["tools"] == null, "Nommage : fournisseur dédié, titre nettoyé, sans outils");
            await db.Entry(chat).ReloadAsync();
            check(chat.Title == title && chat.IsFavorite, "Nommage : titre persisté sans effacer le favori");
            int calls = handler.Calls;
            await ConversationNaming.RenameAsync(database, chat.Id, http, (_, _) => Task.FromResult("private-key"), true, default);
            check(handler.Calls == calls, "Nommage : une conversation déjà nommée n’est pas renommée automatiquement");
            handler.BeforeReply = async () => { await using var concurrent = new HarnessDb(database); await concurrent.Chats.Where(c => c.Id == chat.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Title, "Titre manuel")); };
            var late = await ConversationNaming.RenameAsync(database, chat.Id, http, (_, _) => Task.FromResult("private-key"), false, default);
            await db.Entry(chat).ReloadAsync();
            check(late == null && chat.Title == "Titre manuel", "Nommage : réponse tardive ne remplace pas un renommage manuel");

            features.LogsEnabled = true; features.LogLevel = "Warning"; features.LogRetentionDays = 7;
            AppLog.Configure(features); await AppLog.FlushAsync();
            string logs = AppLog.DirectoryPath;
            await File.WriteAllTextAsync(Path.Combine(logs, "omh-old.jsonl"), "old"); File.SetLastWriteTimeUtc(Path.Combine(logs, "omh-old.jsonl"), DateTime.UtcNow.AddDays(-8));
            await File.WriteAllTextAsync(Path.Combine(logs, "unrelated.txt"), "keep");
            AppLog.Configure(features); AppLog.Write(AppLogLevel.Debug, "filtered-debug");
            AppLog.Write(AppLogLevel.Warning, "retained-warning", new InvalidOperationException("PRIVATE-SECRET-MESSAGE")); await AppLog.FlushAsync();
            string logText = string.Join("", Directory.GetFiles(logs, "omh-*.jsonl").Select(File.ReadAllText));
            check(!File.Exists(Path.Combine(logs, "omh-old.jsonl")) && File.Exists(Path.Combine(logs, "unrelated.txt")), "Logs : purge sept jours limitée aux logs de l’application");
            check(logText.Contains("retained-warning") && !logText.Contains("filtered-debug") && !logText.Contains("PRIVATE-SECRET-MESSAGE"), "Logs : sévérité filtrée et aucun message d’exception sensible");
            features.LogsEnabled = false; AppLog.Configure(features); AppLog.Write(AppLogLevel.Critical, "disabled-event"); await AppLog.FlushAsync();
            check(!string.Join("", Directory.GetFiles(logs, "omh-*.jsonl").Select(File.ReadAllText)).Contains("disabled-event"), "Logs : désactivation immédiate des nouvelles entrées");
        }
        finally
        {
            await AppLog.FlushAsync(); PortableStorage.UseDatabase(Path.Combine(previous, "database.sqlite")); AppLog.Configure(new FeatureSettings { LogsEnabled = false });
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true);
        }
    }
    sealed class NamingHandler : HttpMessageHandler
    {
        public int Calls;
        public JsonNode? Payload;
        public Func<Task>? BeforeReply;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Payload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (BeforeReply != null) await BeforeReply();
            string json = System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = "\"Jeu de cartes tactique\"" }, finish_reason = "stop" } } });
            return new(HttpStatusCode.OK) { Content = new StringContent("data: " + json + "\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
}

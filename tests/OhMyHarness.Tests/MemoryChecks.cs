using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

static class MemoryChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "omh-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "database.sqlite");
        async Task Fails<T>(Func<Task> work, string label) where T : Exception
        {
            try { await work(); } catch (T) { check(true, label); return; }
            throw new Exception("Exception attendue : " + label);
        }
        try
        {
            await using var db = new HarnessDb(path);
            await db.Database.MigrateAsync();
            var p = new Project { Name = "Memory project", Chats = [new Chat { Title = "A" }, new Chat { Title = "B" }] };
            var other = new Project { Name = "Other project", Chats = [new Chat { Title = "C" }] };
            db.Projects.AddRange(p, other); await db.SaveChangesAsync();
            // Seed with the current entity model, then downgrade the fixture to the pre-memory schema.
            await db.GetService<IMigrator>().MigrateAsync("20260922172725_ScheduledTasksAndProviderModels");
            await db.InitializeAsync();
            check(await db.Chats.CountAsync() == 3 && !db.Database.HasPendingModelChanges(), "Mémoire : migration EF conserve les chats et concorde avec le modèle");
            var a = new MemoryAccess(p.Id, p.Chats[0].Id); var b = new MemoryAccess(p.Id, p.Chats[1].Id); var c = new MemoryAccess(other.Id, other.Chats[0].Id);
            var store = new MemoryStore(path);
            var privateEntry = await store.SaveAsync(a, new("conversation", "user", "style", "Préférences", "Répondre avec des exemples concis.", "français style"));
            var projectEntry = await store.SaveAsync(a, new("shared", "project", "build", "Validation", "Compiler avec dotnet build.", "validation"));
            var sharedEntry = await store.SaveAsync(a, new("shared", "user", "language", "Langue", "Préférer le français.", "préférences langue"));
            var general = await store.SaveAsync(a, new("shared", "general", "principle", "Principe", "Vérifier les faits avant de conclure."));
            check((await store.SearchAsync(a)).Items.Count == 4 && (await store.SearchAsync(b)).Items.Count == 3 && (await store.SearchAsync(c)).Items.Count == 2, "Mémoire : isolation des conversations et projets, partage général/utilisateur");
            check((await store.SearchAsync(a, "prefer", category: "user")).Items.Count == 2, "Mémoire : recherche FTS par préfixe sans accents dans titres et tags");
            check((await store.SearchAsync(a, "exem conc")).Items.Single().Id == privateEntry.Id, "Mémoire : recherche multi-mots dans le contenu");
            check((await store.SearchAsync(a, "", limit: 2)).HasMore && (await store.SearchAsync(a, "", offset: 2, limit: 2)).Items.Count == 2, "Mémoire : résultats bornés et pagination");
            await Fails<KeyNotFoundException>(() => store.ReadAsync(b, privateEntry.Id), "Mémoire : lecture par ID ne contourne pas la portée");
            await Fails<KeyNotFoundException>(() => store.ReadAsync(c, projectEntry.Id), "Mémoire : lecture interprojet refusée");
            await Fails<UnauthorizedAccessException>(() => store.SaveAsync(a with { Shared = false }, new("shared", "user", "no", "No", "No")), "Mémoire : écriture partagée désactivée refusée");
            await Fails<InvalidOperationException>(() => store.SaveAsync(a, new("conversation", "user", "style", "Doublon", "Doublon")), "Mémoire : déduplication par clé et portée");
            var updated = await store.SaveAsync(a, new("conversation", "user", "style", "Nouveau titre", "Contenu actualisé.", "nouveau"), privateEntry.Id, privateEntry.Version);
            check(updated.Version == 2 && (await store.SearchAsync(a, "actualise")).Items.Single().Id == privateEntry.Id && (await store.SearchAsync(a, "exemples")).Items.Count == 0, "Mémoire : modification et index FTS synchronisés");
            await Fails<InvalidOperationException>(() => store.SaveAsync(a, new("conversation", "user", "style", "Ancienne version", "Conflit"), privateEntry.Id, 1), "Mémoire : version obsolète refusée");
            await Fails<InvalidOperationException>(() => store.DeleteAsync(a, privateEntry.Id, 1), "Mémoire : suppression obsolète refusée");
            await Fails<InvalidOperationException>(() => store.DeleteAsync(c, projectEntry.Id, projectEntry.Version), "Mémoire : suppression interprojet refusée");
            var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
            {
                try { await store.SaveAsync(a, new("shared", "project", "build", "Concurrent", "Version " + i), projectEntry.Id, 1); return true; }
                catch (InvalidOperationException) { return false; }
            }));
            check(concurrent.Count(x => x) == 1, "Mémoire : deux agents ne peuvent pas écraser une même version");
            var definitions = new JsonArray(); MemoryTools.AddDefinitions(definitions, MemoryTools.ConversationSkill);
            AgentPolicy.Filter(definitions, "plan");
            check(definitions.Count == 2 && AgentPolicy.Allowed("plan", "memory_read") && !AgentPolicy.Allowed("plan", "memory_save"), "Mémoire : mode Plan techniquement limité à la lecture");
            var options = new AppState { EnabledSkills = MemoryTools.ConversationSkill + "," + MemoryTools.SharedSkill };
            using var run = new ConversationSession(p.Chats[0], p, new Provider(), options, "", [], path);
            var runtime = new AgentRuntime(run, new CustomSkills(Path.Combine(folder, "skills")), (_, _, _) => throw new InvalidOperationException("No API call expected"),
                (_, _, _) => Task.FromResult(true), _ => Task.CompletedTask, _ => Task.FromResult(options.EnabledSkills));
            var nativeTools = new JsonArray(); runtime.AddDefinitions(nativeTools);
            check(nativeTools.Count(x => MemoryTools.Handles(x?["function"]?["name"]?.GetValue<string>() ?? "")) == 4, "Mémoire : quatre outils exposés par le runtime natif");
            var searched = JsonNode.Parse(await runtime.CallAsync("memory_search", new JsonObject { ["query"] = "francais" }, default));
            check(searched?["items"]?.AsArray().Count == 1, "Mémoire : lecture via le dispatcher des agents");
            var nativeSaved = JsonNode.Parse(await runtime.CallAsync("memory_save", new JsonObject { ["scope"] = "conversation", ["category"] = "general", ["key"] = "native", ["title"] = "Native", ["content"] = "Saved through the agent runtime" }, default));
            check(nativeSaved?["id"]?.GetValue<int>() > 0, "Mémoire : écriture autorisée via le dispatcher des agents");
            var args = new JsonObject { ["scope"] = "shared", ["category"] = "general", ["key"] = "refused", ["title"] = "Refused", ["content"] = "No write" };
            await MemoryTools.CallAsync(run, "memory_save", args, _ => Task.FromResult(options.EnabledSkills), (_, _, _, _) => Task.FromResult(false), default);
            check((await store.SearchAsync(a, "refused")).Items.Count == 0, "Mémoire : refus d’autorisation sans écriture");
            await Fails<UnauthorizedAccessException>(() => MemoryTools.CallAsync(run, "memory_save", args, _ => Task.FromResult(options.EnabledSkills), (_, _, _, _) => { options.EnabledSkills = ""; return Task.FromResult(true); }, default), "Mémoire : désactivation du skill pendant l’autorisation respectée");
            await store.DeleteAsync(a, privateEntry.Id, updated.Version);
            check((await store.SearchAsync(a, "actualise")).Items.Count == 0, "Mémoire : suppression propagée à l’index");
            await db.Chats.Where(x => x.Id == a.ChatId).ExecuteDeleteAsync();
            check((await store.ReadAsync(b, sharedEntry.Id)).OriginChatId == null, "Mémoire : supprimer le fil d’origine conserve les faits partagés");
            await db.Projects.Where(x => x.Id == p.Id).ExecuteDeleteAsync();
            check((await store.SearchAsync(c)).Items.Count == 2, "Mémoire : suppression projet nettoie sa mémoire et préserve les mémoires globales");
            check((await store.SearchAsync(c, "francais")).Items.Single().Id == sharedEntry.Id, "Mémoire : index FTS valide après cascades");
            await db.GetService<IMigrator>().MigrateAsync("20260922172725_ScheduledTasksAndProviderModels");
            await db.Database.MigrateAsync();
            check((await store.SearchAsync(c)).Items.Count == 0, "Mémoire : migration réversible et index recréé");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }
}

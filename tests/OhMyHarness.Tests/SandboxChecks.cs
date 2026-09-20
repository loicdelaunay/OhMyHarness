using OhMyHarness.Core;
using System.Formats.Tar;
using System.Text;
using System.Text.Json.Nodes;

static class SandboxChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        async Task Reject(Func<Task> action, string label)
        {
            bool rejected = false;
            try { await action(); } catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or IOException or ArgumentException) { rejected = true; }
            check(rejected, label);
        }
        var root = Path.Combine(Path.GetTempPath(), "omh-sandbox-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
            await File.WriteAllTextAsync(Path.Combine(project, "app.js"), "before\n");
            await File.WriteAllTextAsync(Path.Combine(project, "delete.txt"), "remove me\n");
            await File.WriteAllTextAsync(Path.Combine(project, ".env"), "secret");
            await File.WriteAllTextAsync(Path.Combine(project, "database.sqlite"), "sqlite secret");
            Directory.CreateDirectory(Path.Combine(project, ".git"));
            await File.WriteAllTextAsync(Path.Combine(project, ".git", "config"), "private config");
            var database = Path.Combine(root, "database.sqlite");
            string work;
            using (var sandbox = await SandboxWorkspace.OpenAsync(database, 42, [project], default))
            {
                work = sandbox.WorkRoots[0];
                check(await File.ReadAllTextAsync(Path.Combine(work, "app.js")) == "before\n", "Sandbox copie les sources");
                check(!File.Exists(Path.Combine(work, ".env")) && !File.Exists(Path.Combine(work, "database.sqlite")) && !Directory.Exists(Path.Combine(work, ".git")), "Sandbox exclut secrets, SQLite et métadonnées Git");
                await Reject(async () => { using var busy = await SandboxWorkspace.OpenAsync(database, 42, [project], default); }, "Sandbox verrouillée par conversation");
                using (var independent = await SandboxWorkspace.OpenAsync(database, 43, [project], default))
                    check(independent.WorkRoots[0] != work, "Conversations sandbox indépendantes");
                await File.WriteAllTextAsync(Path.Combine(work, "app.js"), "after\n");
                await File.WriteAllTextAsync(Path.Combine(work, "created.md"), "new\n");
                File.Delete(Path.Combine(work, "delete.txt"));
                check(await File.ReadAllTextAsync(Path.Combine(project, "app.js")) == "before\n", "Écriture sandbox ne touche pas le projet réel");
                var review = await sandbox.ReviewAsync(default);
                check(review.Count == 3 && review.Diff.Contains("+after") && review.Diff.Contains("-before"), "Diff sandbox couvre créations, modifications et suppressions");
                await File.WriteAllTextAsync(Path.Combine(project, "app.js"), "external change\n");
                await Reject(() => sandbox.ApplyAsync(review, default), "Conflit avec changement du projet réel refusé");
                check(!File.Exists(Path.Combine(project, "created.md")) && File.Exists(Path.Combine(project, "delete.txt")), "Conflit ne produit pas d'application partielle");
                await File.WriteAllTextAsync(Path.Combine(project, "app.js"), "before\n");
                await sandbox.ApplyAsync(review, default);
                check(await File.ReadAllTextAsync(Path.Combine(project, "app.js")) == "after\n" && !File.Exists(Path.Combine(project, "delete.txt")) && File.Exists(Path.Combine(project, "created.md")), "Validation applique les changements exacts");
                check((await sandbox.ReviewAsync(default)).Count == 0, "Baseline avancée après application");
                var exported = await sandbox.ExportAsync(default);
                await sandbox.ImportAsync(new MemoryStream(exported), default);
                check((await sandbox.ReviewAsync(default)).Count == 0, "Archive sandbox aller-retour sans modification");
                foreach (var name in new[] { "../escape.js", "/absolute.js", "project/../../escape.js", "project/C:ads.js", "project/CON.js", "project\\escape.js" })
                {
                    await Reject(() => sandbox.ImportAsync(Archive(name), default), "Extraction refuse " + name);
                    check(File.Exists(Path.Combine(work, "app.js")), "Archive invalide conserve les fichiers précédents");
                }
                await Reject(() => sandbox.ImportAsync(Archive("project/link.js", TarEntryType.SymbolicLink), default), "Liens symboliques du conteneur refusés");
                await Reject(() => sandbox.ImportAsync(Archive("unknown/app.js"), default), "Racine inattendue refusée");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                try { await sandbox.ImportAsync(new MemoryStream(exported), cancelled.Token); } catch (OperationCanceledException) { }
                check(File.Exists(Path.Combine(work, "app.js")), "Annulation import conserve la copie");
            }
            using (var reopened = await SandboxWorkspace.OpenAsync(database, 42, [project], default))
            {
                check(await File.ReadAllTextAsync(Path.Combine(reopened.WorkRoots[0], "app.js")) == "after\n", "Copie sandbox conservée entre les tours");
                using var forkA = await SandboxWorkspace.OpenAsync(Path.Combine(root, "fork-a", "db.sqlite"), 1, reopened.WorkRoots, default);
                using var forkB = await SandboxWorkspace.OpenAsync(Path.Combine(root, "fork-b", "db.sqlite"), 1, reopened.WorkRoots, default);
                await File.WriteAllTextAsync(Path.Combine(forkA.WorkRoots[0], "app.js"), "job A");
                await File.WriteAllTextAsync(Path.Combine(forkB.WorkRoots[0], "created.md"), "job B");
                await forkB.ApplyAsync(await forkB.ReviewAsync(default), default);
                await forkA.ApplyAsync(await forkA.ReviewAsync(default), default);
                check(await File.ReadAllTextAsync(Path.Combine(reopened.WorkRoots[0], "app.js")) == "job A" && await File.ReadAllTextAsync(Path.Combine(reopened.WorkRoots[0], "created.md")) == "job B", "Commandes sandbox indépendantes fusionnées sans perte des changements voisins");
                await File.WriteAllTextAsync(Path.Combine(forkB.WorkRoots[0], "app.js"), "conflicting B");
                await Reject(async () => await forkB.ApplyAsync(await forkB.ReviewAsync(default), default), "Conflit entre commandes sandbox refusé");
                check(await File.ReadAllTextAsync(Path.Combine(project, "app.js")) == "after\n", "Fusion des terminaux sandbox ne touche jamais le projet réel");
            }
            var nested = Path.Combine(project, "nested"); Directory.CreateDirectory(nested);
            await Reject(async () => { using var overlapping = await SandboxWorkspace.OpenAsync(database, 44, [project, nested], default); }, "Racines sources imbriquées refusées");
            foreach (var tool in new[] { "desktop_keyboard", "desktop_mouse", "browse", "open_local_file", "mcp_anything", "unknown" })
                await Reject(() => { SandboxWorkspace.Demand(true, tool); return Task.CompletedTask; }, "Frontière sandbox refuse " + tool);
            var definitions = new JsonArray(new JsonObject { ["function"] = new JsonObject { ["name"] = "browse" } }, new JsonObject { ["function"] = new JsonObject { ["name"] = "run_terminal" } });
            SandboxWorkspace.Filter(definitions, true);
            check(definitions.Count == 1 && definitions[0]!["function"]!["description"]!.GetValue<string>().Contains("Linux"), "Catalogue sandbox annonce Linux et filtre outils extérieurs");
            var args = SandboxContainer.StartArguments("fixture");
            check(args.Contains("--network=none") && args.Contains("--cap-drop=ALL") && args.Contains("--read-only") && args.Contains("--user=65534:65534") && args.Contains("--memory=512m") && !args.Any(x => x.Contains("type=bind") || x.Contains("docker.sock")), "Commande conteneur isolée sans montage hôte et avec limites");
        }
        finally { Directory.Delete(root, true); }
    }
    static MemoryStream Archive(string name, TarEntryType type = TarEntryType.RegularFile)
    {
        var memory = new MemoryStream();
        using (var writer = new TarWriter(memory, leaveOpen: true))
        {
            var entry = new PaxTarEntry(type, name);
            if (type == TarEntryType.SymbolicLink) entry.LinkName = "/etc/passwd";
            else entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("untrusted"));
            writer.WriteEntry(entry);
        }
        memory.Position = 0; return memory;
    }
}

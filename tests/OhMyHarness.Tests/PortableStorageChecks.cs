using OhMyHarness.Core;

static class PortableStorageChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var previous = HarnessDb.DatabasePath;
        var root = Path.Combine(Path.GetTempPath(), "omh-portable-" + Guid.NewGuid().ToString("N"));
        try
        {
            var destination = Path.Combine(root, "portable");
            PortableStorage.UseDatabase(Path.Combine(destination, "database.sqlite"));
            PortableStorage.EnsureWritable();
            check(HarnessDb.DataDirectory == destination && CustomSkills.DefaultRoot == Path.Combine(destination, "skills") && PortableStorage.Temporary == Path.Combine(destination, "temp"), "Service : ressources regroupées dans le dossier de la base du host");
            new CustomSkills(CustomSkills.DefaultRoot).EnsureTemplate();
            check(File.Exists(Path.Combine(destination, "skills", "exemple-revue", "SKILL.md")), "Template créé dans les skills portables");
            var legacy = Path.Combine(root, "legacy"); Directory.CreateDirectory(Path.Combine(legacy, "nested"));
            File.WriteAllText(Path.Combine(legacy, "nested", "state.txt"), "original");
            PortableStorage.ImportLegacyDirectory(legacy, "WebView2");
            var imported = Path.Combine(destination, "WebView2", "nested", "state.txt");
            check(File.ReadAllText(imported) == "original" && Directory.Exists(legacy), "Import du profil existant sans supprimer la sauvegarde");
            File.WriteAllText(imported, "new");
            PortableStorage.ImportLegacyDirectory(legacy, "WebView2");
            check(File.ReadAllText(imported) == "new", "Profil portable existant jamais écrasé par le profil historique");
            await using (var first = new HarnessDb())
            {
                await first.InitializeAsync();
                first.Messages.Add(new Message { ChatId = first.Chats.Single().Id, Content = "conversation du premier dossier" });
                await first.SaveChangesAsync();
            }
            var freshFolder = Path.Combine(root, "nouvel-exe");
            PortableStorage.UseDatabase(Path.Combine(freshFolder, "database.sqlite"));
            await using (var fresh = new HarnessDb())
            {
                await fresh.InitializeAsync();
                check(File.Exists(Path.Combine(freshFolder, "database.sqlite")) && fresh.Messages.Count() == 0 && fresh.Projects.Count() == 1,
                    "Nouvel EXE : base locale initialisée sans conversation précédente");
            }
            check(File.Exists(Path.Combine(destination, "database.sqlite")), "Ancienne base portable conservée dans son propre dossier");
        }
        finally
        {
            PortableStorage.UseDatabase(previous);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

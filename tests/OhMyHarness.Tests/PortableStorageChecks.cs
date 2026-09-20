using OhMyHarness.Core;

static class PortableStorageChecks
{
    public static void Run(Action<bool, string> check)
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
        }
        finally
        {
            PortableStorage.UseDatabase(previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

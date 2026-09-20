namespace OhMyHarness.Core;

/// <summary>Application-owned files share the host's portable database directory.</summary>
public static class PortableStorage
{
    // IncludeAllContentForSelfExtract redirects BaseDirectory into the extraction cache.
    // ProcessPath still identifies the actual apphost executable selected by the user.
    public static string Root { get; private set; } = Path.GetDirectoryName(Environment.ProcessPath
        ?? throw new InvalidOperationException("Impossible de déterminer le chemin de l’exécutable."))!;
    public static string Skills => Path.Combine(Root, "skills");
    public static string Temporary => Path.Combine(Root, "temp");

    // The desktop host supplies its database path; the service lives inside Resources.
    public static void UseDatabase(string database) => Root = Path.GetDirectoryName(Path.GetFullPath(database))!;

    public static void EnsureWritable()
    {
        Directory.CreateDirectory(Root);
        var probe = Path.Combine(Root, ".write-test-" + Guid.NewGuid().ToString("N"));
        try { using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Le dossier de l’application doit être accessible en écriture. Déplacez le dossier complet vers un emplacement personnel. / The application folder must be writable: " + Root, ex);
        }
    }

    // Copy first, then publish the completed directory. Never overwrite a portable profile.
    public static void ImportLegacyDirectory(string source, string name)
    {
        var destination = Path.Combine(Root, name);
        if (Directory.Exists(destination) || !Directory.Exists(source)) return;
        var staging = destination + ".import-" + Guid.NewGuid().ToString("N");
        Copy(source, staging);
        Directory.Move(staging, destination);
        static void Copy(string source, string target)
        {
            SandboxWorkspace.AssertNoLinks(source);
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                SandboxWorkspace.AssertNoLinks(file);
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            }
            foreach (var folder in Directory.EnumerateDirectories(source)) Copy(folder, Path.Combine(target, Path.GetFileName(folder)));
        }
    }
}

using System.Diagnostics;
using System.Text;

namespace OhMyHarness.Core;

public static class WorkspaceTools
{
    public static async Task<string> ExecuteAsync(string executable, IEnumerable<string> arguments, string directory, CancellationToken ct)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = new Process { StartInfo = start };
        process.Start(); process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        async Task<string> ReadBounded(StreamReader reader)
        {
            var result = new StringBuilder(); var buffer = new char[4096]; int count;
            while ((count = await reader.ReadAsync(buffer, timeout.Token)) > 0)
                if (result.Length < 100_000) result.Append(buffer, 0, Math.Min(count, 100_000 - result.Length));
            return result.ToString() + (result.Length == 100_000 ? "\n[output truncated]" : "");
        }
        var stdout = ReadBounded(process.StandardOutput); var stderr = ReadBounded(process.StandardError);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr);
            return $"Exit code: {process.ExitCode}\n{await stdout}\n{await stderr}";
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
    }
    public static Task<string> PowerShellAsync(string command, string directory, CancellationToken ct) => ExecuteAsync("powershell.exe",
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new(); " + command], directory, ct);
    public static Task<string> GitAsync(string directory, IEnumerable<string> args, CancellationToken ct) => ExecuteAsync("git", args, directory, ct);
}

public static class LocalPreview
{
    public static string ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) throw new UnauthorizedAccessException("Only absolute local paths are supported.");
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains(':')) throw new UnauthorizedAccessException("Alternate data streams are not supported.");
            current = Path.Combine(current, part);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Symbolic links and junctions are not supported.");
        }
        return full;
    }
    public static string ResolveResource(string folder, string escapedPath)
    {
        var relative = Uri.UnescapeDataString(escapedPath.TrimStart('/'));
        var path = new SourceAccess(folder).Resolve(relative);
        ValidatePath(path);
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new IOException("Preview file too large (32 MB).");
        return path;
    }
    public static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8", ".css" => "text/css; charset=utf-8", ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json", ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp",
        ".gif" => "image/gif", ".ico" => "image/x-icon", ".pdf" => "application/pdf", ".woff" => "font/woff", ".woff2" => "font/woff2",
        ".txt" or ".md" or ".cs" or ".ts" or ".xml" or ".yaml" or ".yml" => "text/plain; charset=utf-8",
        _ => "application/octet-stream"
    };
}

public static class DefaultTemplates
{
    public const string WebApp = """
        Crée de zéro une application web autonome dans un seul fichier index.html.

        Objectif de l’application : [décris ton idée]
        Public cible : [à préciser]
        Fonctionnalités essentielles :
        - [fonctionnalité 1]
        - [fonctionnalité 2]
        - [fonctionnalité 3]

        Contraintes :
        - Tout le HTML, le CSS et le JavaScript dans un seul fichier, sans CDN, dépendance externe, compilation ni backend.
        - Fonctionnement hors ligne en ouvrant directement index.html dans un navigateur.
        - Interface moderne, responsive, accessible au clavier ; états vides et erreurs explicites.
        - Sauvegarde locale si nécessaire avec localStorage, avec gestion de son indisponibilité.
        - Aucune clé API ni secret dans le fichier.
        - Fonctionnalités réellement utilisables, sans boutons factices.

        Pose les questions indispensables si le besoin est incomplet. Fournis ensuite le fichier complet. Si un dossier source est associé et l’écriture autorisée, crée index.html dedans. Demande l’autorisation avant d’ouvrir le fichier dans le navigateur intégré pour le vérifier.
        """;
}

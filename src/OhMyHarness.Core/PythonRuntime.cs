using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace OhMyHarness.Core;

public static class PythonRuntime
{
    public sealed record Manifest(string Version, string Release, string Rid, string Sha256);
    static readonly SemaphoreSlim Gate = new(1, 1);
    public static Manifest Bundle()
    {
        using var stream = Resource("manifest.json");
        var info = JsonSerializer.Deserialize<Manifest>(stream) ?? throw new IOException("Python manifest missing.");
        var rid = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "unsupported") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        if (info.Rid != rid) throw new PlatformNotSupportedException($"Python embarqué pour {info.Rid}, processus {rid}.");
        return info;
    }
    static Stream Resource(string name) => Assembly.GetEntryAssembly()?.GetManifestResourceStream("OhMyHarness.Python." + name)
        ?? throw new IOException("Runtime Python absent de cette compilation. Compilez/publiez l’application avec build/PythonRuntime.targets.");
    public static string DirectoryPath(Manifest info) => Path.Combine(PortableStorage.Root, "runtimes", "python", $"{info.Version}-{info.Release}-{info.Rid}");
    public static string Executable(Manifest info) => Path.Combine(DirectoryPath(info), "python", OperatingSystem.IsWindows() ? "python.exe" : "bin/python3.13");
    public static async Task<string> EnsureAsync(CancellationToken ct)
    {
        var info = Bundle(); var root = DirectoryPath(info); var executable = Executable(info);
        await Gate.WaitAsync(ct);
        try
        {
            SandboxWorkspace.AssertNoLinks(root);
            if (File.Exists(Path.Combine(root, ".complete")) && File.Exists(executable)) return executable;
            if (Directory.Exists(root)) throw new IOException("Runtime Python incomplet : renommez ce dossier pour permettre une nouvelle extraction : " + root);
            PortableStorage.EnsureWritable(); Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            var staging = root + ".extract-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try
            {
                // Verify the embedded bytes too, then reopen the resource for streaming extraction.
                using (var archive = Resource("archive.tar.gz"))
                {
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(archive, ct));
                    if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Empreinte du runtime Python invalide.");
                }
                using var packed = Resource("archive.tar.gz");
                using var gzip = new GZipStream(packed, CompressionMode.Decompress);
                using var tar = new TarReader(gzip);
                var links = new List<(string Path, string Target, bool Hard)>();
                long total = 0;
                while (await tar.GetNextEntryAsync(cancellationToken: ct) is { } entry)
                {
                    var path = Inside(staging, entry.Name);
                    if (entry.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(path); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                    {
                        var target = entry.EntryType == TarEntryType.HardLink ? Inside(staging, entry.LinkName)
                            : Inside(staging, Path.GetRelativePath(staging, Path.Combine(Path.GetDirectoryName(path)!, entry.LinkName)));
                        links.Add((path, target, entry.EntryType == TarEntryType.HardLink)); continue;
                    }
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new IOException("Type d’entrée Python non pris en charge.");
                    total += entry.Length; if (total > 1_000_000_000) throw new IOException("Archive Python trop volumineuse.");
                    await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                        if (entry.DataStream != null) await entry.DataStream.CopyToAsync(output, ct);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1FF);
                }
                // Links are created last so no archive entry can write through a link.
                foreach (var link in links)
                {
                    SandboxWorkspace.AssertNoLinks(Path.GetDirectoryName(link.Path)!);
                    if (link.Hard) File.Copy(link.Target, link.Path);
                    else File.CreateSymbolicLink(link.Path, Path.GetRelativePath(Path.GetDirectoryName(link.Path)!, link.Target));
                }
                await File.WriteAllTextAsync(Path.Combine(staging, ".complete"), info.Sha256, ct);
                // Antivirus scanners can briefly lock freshly extracted DLLs on Windows.
                // Publish the complete directory atomically, retrying only transient filesystem locks.
                for (int attempt = 0; ; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try { Directory.Move(staging, root); break; }
                    catch (IOException) when (File.Exists(Path.Combine(root, ".complete")) && File.Exists(executable)) { break; }
                    catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException)
                    { await Task.Delay(250, ct); }
                }
                if (!File.Exists(executable)) throw new IOException("Exécutable Python introuvable après extraction.");
                return executable;
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        finally { Gate.Release(); }
    }
    static string Inside(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("Absolute archive path rejected.");
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, PlatformSupport.PathComparison)) throw new IOException("Archive path escapes runtime directory.");
        return target;
    }
}

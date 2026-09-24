using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public enum UpdateChannel { Gui, Cli }
public sealed record GitHubUpdate(string Version, string Tag, string Page, string AssetName, string DownloadUrl, string Sha256, long Size);

public sealed class GitHubUpdates(HttpClient http)
{
    public const string CurrentVersion = "1.8.0";
    public const string Repository = "https://github.com/loicdelaunay/OhMyHarness";
    public const string Api = "https://api.github.com/repos/loicdelaunay/OhMyHarness/releases";
    const long MaxBytes = 1024L * 1024 * 1024;
    public static string Runtime => "win-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
    public static string Executable(UpdateChannel channel) => channel == UpdateChannel.Cli ? "omh.exe" : "OhMyHarness.App.exe";
    public static bool CanInstall => OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;
    public static bool IsStandalone(System.Reflection.Assembly assembly) => assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>().Any(a => a.Key == "OhMyHarness.SingleFile" && a.Value == "true");

    public async Task<GitHubUpdate?> CheckAsync(UpdateChannel channel, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        GitHubUpdate? best = null;
        for (int page = 1; page <= 5; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Api + "?per_page=100&page=" + page);
            request.Headers.UserAgent.ParseAdd("OhMyHarness/" + CurrentVersion);
            using var response = await http.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
            var releases = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token))!.AsArray();
            var candidate = Select(releases, channel, CurrentVersion, Runtime);
            if (candidate != null && (best == null || Version.Parse(candidate.Version) > Version.Parse(best.Version))) best = candidate;
            if (releases.Count < 100) break;
        }
        return best;
    }

    // Match channel, version and architecture instead of relying on GitHub's global "latest" flag.
    public static GitHubUpdate? Select(JsonArray releases, UpdateChannel channel, string currentVersion, string runtime)
    {
        var candidates = new List<GitHubUpdate>();
        foreach (var release in releases)
        {
            if (release?["draft"]?.GetValue<bool>() == true || release?["prerelease"]?.GetValue<bool>() == true) continue;
            string tag = release?["tag_name"]?.ToString() ?? "";
            var match = Regex.Match(tag, channel == UpdateChannel.Cli ? @"^cli-v(\d+\.\d+\.\d+)$" : @"^(?:gui-)?v(\d+\.\d+\.\d+)$");
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version) || version <= Version.Parse(currentVersion)) continue;
            foreach (var asset in release?["assets"]?.AsArray() ?? [])
            {
                string name = asset?["name"]?.ToString() ?? "", url = asset?["browser_download_url"]?.ToString() ?? "";
                string digest = asset?["digest"]?.ToString() ?? "";
                long size = asset?["size"]?.GetValue<long>() ?? 0;
                string prefix = channel == UpdateChannel.Cli ? "OhMyHarness-CLI-" : "OhMyHarness-";
                bool nameMatches = name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && (channel == UpdateChannel.Cli || !name.StartsWith("OhMyHarness-CLI-", StringComparison.OrdinalIgnoreCase))
                    && (name.EndsWith("-" + runtime + ".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-" + runtime + ".exe", StringComparison.OrdinalIgnoreCase));
                if (!nameMatches || size is <= 0 or > MaxBytes || !OfficialAssetUrl(url) || !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")) continue;
                candidates.Add(new(version.ToString(), tag, Repository + "/releases/tag/" + tag, name, url, digest[7..].ToLowerInvariant(), size));
            }
        }
        return candidates.OrderByDescending(c => Version.Parse(c.Version)).FirstOrDefault();
    }
    static bool OfficialAssetUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com"
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath.StartsWith("/loicdelaunay/OhMyHarness/releases/download/", StringComparison.Ordinal);

    public async Task<string> DownloadAsync(GitHubUpdate update, UpdateChannel channel, string executable, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (!OfficialAssetUrl(update.DownloadUrl) || !Regex.IsMatch(update.Sha256, "^[0-9a-fA-F]{64}$") || update.Size is <= 0 or > MaxBytes) throw new InvalidDataException("Invalid update asset.");
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, ".updates", channel.ToString().ToLowerInvariant(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string archive = Path.Combine(folder, "download.bin"), staged = Path.Combine(folder, Executable(channel));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl); request.Headers.UserAgent.ParseAdd("OhMyHarness/" + CurrentVersion);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token); response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(archive))
            {
                byte[] buffer = new byte[81920]; long total = 0; int read, lastPercent = -1;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read; if (total > update.Size || total > MaxBytes) throw new InvalidDataException("Update exceeds expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    int percent = (int)(total * 100 / update.Size);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                }
                if (total != update.Size) throw new InvalidDataException("Incomplete update download.");
            }
            await using (var file = File.OpenRead(archive))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token)).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 verification failed.");
            if (update.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                await ExtractExecutableAsync(archive, staged, Executable(channel), timeout.Token);
            else File.Copy(archive, staged);
            // Download verification is required for both ZIP and raw EXE assets.
            using (var exe = File.OpenRead(staged))
                if (exe.ReadByte() != 'M' || exe.ReadByte() != 'Z') throw new InvalidDataException("Update is not a Windows executable.");
            File.Delete(archive);
            return staged;
        }
        catch { Directory.Delete(folder, true); throw; }
    }
    public static async Task ExtractExecutableAsync(string archive, string staged, string expectedName, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        var files = zip.Entries.Where(e => e.FullName.Replace('\\', '/').Split('/').Last().Equals(expectedName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (files.Count != 1 || files[0].Length is <= 0 or > MaxBytes) throw new InvalidDataException("Expected a single executable in the release archive.");
        // Never extract archive paths or data/configuration files, even if present in the ZIP.
        await using var input = files[0].Open(); await using var output = File.Create(staged);
        byte[] buffer = new byte[81920]; long total = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > files[0].Length || total > MaxBytes) throw new InvalidDataException("Extracted executable exceeds expected size.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (total != files[0].Length) throw new InvalidDataException("Incomplete executable in archive.");
    }

    public static void InstallAfterExit(string staged, string executable, IEnumerable<string> restartArguments, bool singleFile)
    {
        if (!CanInstall || !singleFile) throw new InvalidOperationException("Automatic installation requires the published Windows standalone executable.");
        string target = Path.GetFullPath(executable), folder = Path.GetDirectoryName(Path.GetFullPath(staged))!;
        string updateRoot = Path.Combine(Path.GetDirectoryName(target)!, ".updates") + Path.DirectorySeparatorChar;
        if (!folder.StartsWith(updateRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(staged) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid update staging path.");
        string hash;
        using (var file = File.OpenRead(staged)) hash = Convert.ToHexString(SHA256.HashData(file));
        File.WriteAllText(Path.Combine(folder, "install.json"), JsonSerializer.Serialize(new { Target = target, Staged = staged, Hash = hash, Pid = Environment.ProcessId,
            Started = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, WorkingDirectory = Environment.CurrentDirectory, Restart = true,
            Arguments = string.Join(' ', restartArguments.Select(QuoteWindowsArgument)) }), new UTF8Encoding(false));
        var script = Path.Combine(folder, "install.ps1"); File.WriteAllText(script, InstallerScript, new UTF8Encoding(true));
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
        using var helper = Process.Start(start) ?? throw new IOException("Unable to start update installer.");
    }
    public static string QuoteWindowsArgument(string value)
    {
        if (value.Any(char.IsControl)) throw new ArgumentException("Invalid restart argument.");
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value) { if (c == '\\') { slashes++; continue; } text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); text.Append(c); slashes = 0; }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
    public const string InstallerScript = """
        $ErrorActionPreference = 'Stop'
        $job = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'install.json') -Raw | ConvertFrom-Json
        $backup = Join-Path $PSScriptRoot 'previous.exe'
        $lock = $null
        $replaced = $false
        try {
            $parent = Get-Process -Id $job.Pid -ErrorAction SilentlyContinue
            if ($parent -and $parent.StartTime.ToUniversalTime().Ticks -eq $job.Started) {
                if (!$parent.WaitForExit(120000)) { throw 'Application did not close; update cancelled.' }
            }
            $lock = [IO.File]::Open((Join-Path ([IO.Path]::GetDirectoryName($job.Target)) '.updates/install.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
            $deadline = [DateTime]::UtcNow.AddSeconds(60)
            do {
                $others = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $job.Target } catch { $false } })
                if ($others.Count -eq 0) { break }
                if ([DateTime]::UtcNow -gt $deadline) { throw 'Another instance is still running; update cancelled.' }
                Start-Sleep -Milliseconds 500
            } while ($true)
            $sha = [Security.Cryptography.SHA256]::Create()
            $stream = [IO.File]::OpenRead($job.Staged)
            try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($digest -ne $job.Hash) { throw 'Staged executable hash mismatch.' }
            [IO.File]::Replace($job.Staged, $job.Target, $backup)
            $replaced = $true
            if ($job.Restart) {
                $start = New-Object Diagnostics.ProcessStartInfo
                $start.FileName = $job.Target
                $start.Arguments = $job.Arguments
                $start.WorkingDirectory = $job.WorkingDirectory
                $start.UseShellExecute = $true
                [Diagnostics.Process]::Start($start) | Out-Null
            }
            'Update installed. Previous executable: previous.exe' | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'result.txt')
        } catch {
            if ($replaced -and (Test-Path -LiteralPath $backup)) {
                try { [IO.File]::Copy($backup, $job.Target, $true) } catch { }
            }
            $_.Exception.Message | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'error.txt')
        } finally { if ($lock) { $lock.Dispose() } }
        """;
}

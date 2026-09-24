using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OhMyHarness.Core;

static class UpdateChecks
{
    sealed class Download(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    public static async Task Run(Action<bool, string> check)
    {
        JsonObject Release(string tag, string name, bool preview = false) => new() {
            ["tag_name"] = tag, ["prerelease"] = preview, ["draft"] = false,
            ["assets"] = new JsonArray(new JsonObject { ["name"] = name, ["size"] = 123L, ["digest"] = "sha256:" + new string('a', 64),
                ["browser_download_url"] = GitHubUpdates.Repository + "/releases/download/" + tag + "/" + name })
        };
        var releases = new JsonArray(Release("cli-v9.0.0", "OhMyHarness-CLI-v9.0.0-win-x64.zip"), Release("v2.0.0", "OhMyHarness-v2.0.0-win-x64.zip"),
            Release("cli-v10.0.0", "OhMyHarness-CLI-v10.0.0-win-x64.zip", true), Release("cli-v8.0.0", "OhMyHarness-CLI-v8.0.0-win-arm64.zip"));
        check(GitHubUpdates.Select(releases, UpdateChannel.Gui, "1.0.0", "win-x64")?.Version == "2.0.0", "Updates: GUI ignores newer CLI releases");
        check(GitHubUpdates.Select(releases, UpdateChannel.Cli, "1.0.0", "win-x64")?.Version == "9.0.0", "Updates: CLI excludes previews and wrong architectures");
        check(GitHubUpdates.Select(releases, UpdateChannel.Cli, "9.0.0", "win-x64") == null, "Updates: no downgrade or same-version install");
        check(GitHubUpdates.Select(releases, UpdateChannel.Cli, "1.0.0", "win-arm64")?.Version == "8.0.0", "Updates: ARM64 selection is separate");
        var bad = Release("cli-v2.0.0", "OhMyHarness-CLI-v2.0.0-win-x64.zip");
        bad["assets"]![0]!["digest"] = null;
        check(GitHubUpdates.Select(new JsonArray(bad.DeepClone()), UpdateChannel.Cli, "1.0.0", "win-x64") == null, "Updates: no unverified assets");
        bad["assets"]![0]!["digest"] = "sha256:" + new string('a', 64);
        bad["assets"]![0]!["browser_download_url"] = "https://example.com/OhMyHarness.zip";
        check(GitHubUpdates.Select(new JsonArray(bad.DeepClone()), UpdateChannel.Cli, "1.0.0", "win-x64") == null, "Updates: foreign downloads rejected");
        string folder = Path.Combine(Path.GetTempPath(), "omh-updates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            string target = Path.Combine(folder, "omh.exe"); await File.WriteAllTextAsync(target, "old executable");
            string database = Path.Combine(folder, "database.sqlite"); await File.WriteAllTextAsync(database, "user data");
            byte[] Archive(bool duplicate = false)
            {
                using var output = new MemoryStream();
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    using (var exe = zip.CreateEntry("app/omh.exe").Open()) exe.Write([77, 90, 1, 2]);
                    using (var data = new StreamWriter(zip.CreateEntry("database.sqlite").Open())) data.Write("do not install");
                    using (var traversal = new StreamWriter(zip.CreateEntry("../../escape.txt").Open())) traversal.Write("do not extract");
                    if (duplicate) { using var second = zip.CreateEntry("other/omh.exe").Open(); second.Write([77, 90]); }
                }
                return output.ToArray();
            }
            var bytes = Archive();
            using var http = new HttpClient(new Download(bytes));
            var updater = new GitHubUpdates(http);
            var update = new GitHubUpdate("2.0.0", "cli-v2.0.0", "", "OhMyHarness-CLI-v2.0.0-win-x64.zip", GitHubUpdates.Repository + "/releases/download/cli-v2.0.0/test.zip", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
            string staged = await updater.DownloadAsync(update, UpdateChannel.Cli, target);
            check((await File.ReadAllBytesAsync(staged)).SequenceEqual(new byte[] { 77, 90, 1, 2 }), "Updates: extracts only the expected executable");
            check(await File.ReadAllTextAsync(database) == "user data" && await File.ReadAllTextAsync(target) == "old executable" && !File.Exists(Path.Combine(folder, "escape.txt")), "Updates: staging preserves live EXE, data and ignores ZIP traversal entries");
            async Task Reject(Func<Task> action, string name)
            {
                try { await action(); } catch (InvalidDataException) { check(true, name); return; }
                throw new Exception(name);
            }
            await Reject(async () => await updater.DownloadAsync(update with { Sha256 = new string('0', 64) }, UpdateChannel.Cli, target), "Updates: bad checksum prevents install");
            await Reject(async () => await updater.DownloadAsync(update with { Size = bytes.Length + 1 }, UpdateChannel.Cli, target), "Updates: incomplete download rejected");
            string duplicateZip = Path.Combine(folder, "duplicate.zip"); await File.WriteAllBytesAsync(duplicateZip, Archive(true));
            await Reject(() => GitHubUpdates.ExtractExecutableAsync(duplicateZip, Path.Combine(folder, "staged.exe"), "omh.exe"), "Updates: ambiguous archive rejected");
            try { GitHubUpdates.InstallAfterExit(staged, target, [], false); throw new Exception("Development install accepted"); }
            catch (InvalidOperationException) { check(true, "Updates: development builds cannot replace themselves"); }
            check(Directory.GetFiles(folder, "download.bin", SearchOption.AllDirectories).Length == 0, "Updates: temporary downloads cleaned after success and failure");
            if (OperatingSystem.IsWindows())
            {
                async Task<string> InstallFixture(bool corrupt)
                {
                    string jobFolder = Path.Combine(folder, ".updates", "fixture " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(jobFolder);
                    string candidate = Path.Combine(jobFolder, "candidate.exe"); await File.WriteAllTextAsync(candidate, "replacement executable");
                    string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(candidate)));
                    var manifest = new { Target = target, Staged = candidate, Hash = corrupt ? new string('0', 64) : hash, Pid = int.MaxValue, Started = 0L, WorkingDirectory = folder, Arguments = "", Restart = false };
                    await File.WriteAllTextAsync(Path.Combine(jobFolder, "install.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
                    string script = Path.Combine(jobFolder, "install.ps1"); await File.WriteAllTextAsync(script, GitHubUpdates.InstallerScript, new System.Text.UTF8Encoding(true));
                    var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
                    foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(arg);
                    using var process = System.Diagnostics.Process.Start(start)!; await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                    return jobFolder;
                }
                string installed = await InstallFixture(false);
                if (File.Exists(Path.Combine(installed, "error.txt"))) throw new IOException(await File.ReadAllTextAsync(Path.Combine(installed, "error.txt")));
                check(File.Exists(Path.Combine(installed, "result.txt")) && await File.ReadAllTextAsync(target) == "replacement executable"
                    && await File.ReadAllTextAsync(Path.Combine(installed, "previous.exe")) == "old executable"
                    && await File.ReadAllTextAsync(database) == "user data", "Updates: Windows helper atomically replaces only the EXE and keeps rollback/data");
                string rejected = await InstallFixture(true);
                check(File.Exists(Path.Combine(rejected, "error.txt")) && await File.ReadAllTextAsync(target) == "replacement executable", "Updates: helper independently rejects a tampered staged file");
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}

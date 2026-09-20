using System.Diagnostics;
using System.Text;

namespace OhMyHarness.Core;

/// <summary>Ephemeral Linux process boundary: no host mounts, sockets, keys or inherited environment.</summary>
public static class SandboxContainer
{
    public const string Image = "node:22-bookworm";
    public static async Task<string> ExecuteIsolatedAsync(SandboxWorkspace shared, string executable, string command, CancellationToken ct)
    {
        // Each concurrent job starts from a separate copy. Merge only its changed files with preimage checks.
        var temporary = Path.Combine(PortableStorage.Temporary, "sandbox-terminal-" + Guid.NewGuid().ToString("N"));
        bool preserve = false;
        try
        {
            using var isolated = await SandboxWorkspace.OpenAsync(Path.Combine(temporary, "scope.sqlite"), 1, shared.WorkRoots, ct);
            var result = await ExecuteAsync(isolated, executable, command, ct);
            var review = await isolated.ReviewAsync(ct);
            try { if (review.Count > 0) await isolated.ApplyAsync(review, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { preserve = true; throw new IOException("Conflit entre commandes sandbox. Aucune application au projet réel. Copie récupérable : " + isolated.WorkDirectory + "\n" + ex.Message); }
            return result;
        }
        finally
        {
            if (!preserve && Directory.Exists(temporary)) { SandboxWorkspace.AssertNoLinks(temporary); Directory.Delete(temporary, true); }
        }
    }
    public static async Task<string> CheckAsync(CancellationToken ct)
    {
        var errors = new List<string>();
        var candidates = new List<string> { "docker", "podman" };
        // Finder-launched macOS apps often lack /usr/local/bin and Homebrew in PATH.
        var installed = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", "docker.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RedHat", "Podman", "podman.exe") }
            : new[] { "/usr/local/bin/docker", "/opt/homebrew/bin/docker", "/Applications/Docker.app/Contents/Resources/bin/docker", "/usr/local/bin/podman", "/opt/homebrew/bin/podman", "/opt/podman/bin/podman" };
        candidates.AddRange(installed.Where(File.Exists));
        foreach (var executable in candidates.Distinct(PlatformSupport.PathComparer))
        {
            try
            {
                await RunAsync(executable, ["image", "inspect", Image], null, 200000, ct);
                return executable;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add(executable + ": " + ex.Message); }
        }
        throw new InvalidOperationException("Sandbox indisponible. Installez/démarrez Docker Desktop ou Podman (conteneurs Linux), puis téléchargez l'image avec « docker pull " + Image + " » ou « podman pull " + Image + " ». Aucun outil local n'a été lancé.\n" + string.Join('\n', errors));
    }
    public static IReadOnlyList<string> StartArguments(string name) =>
        ["run", "--detach", "--rm", "--name", name, "--pull=never", "--network=none", "--cap-drop=ALL", "--security-opt=no-new-privileges",
         "--cpus=1", "--memory=512m", "--memory-swap=512m", "--pids-limit=128", "--read-only", "--user=65534:65534",
         "--tmpfs", "/workspace:rw,nosuid,nodev,size=128m,uid=1000,gid=1000,mode=0700", "--tmpfs", "/tmp:rw,nosuid,nodev,size=64m,uid=1000,gid=1000,mode=0700",
         "--workdir=/", "--env=HOME=/tmp", "--log-driver=none", "--entrypoint=node", Image, "-e", "setTimeout(()=>process.exit(0),120000)"];

    public static async Task<string> ExecuteAsync(SandboxWorkspace workspace, string executable, string command, CancellationToken ct)
    {
        if (command.Length > 100000) throw new ArgumentException("Command too long.");
        var name = "ohmyharness-" + Guid.NewGuid().ToString("N");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(100));
        var token = deadline.Token;
        try
        {
            await RunAsync(executable, StartArguments(name), null, 10000, token);
            var archive = await workspace.ExportAsync(token);
            await RunAsync(executable, ["exec", "--user=1000:1000", "-i", name, "tar", "--no-same-owner", "--no-same-permissions", "-xf", "-", "-C", "/workspace"], archive, 10000, token);
            // argv carries untrusted command text only to sh INSIDE the container, never to a host shell.
            const string supervisor = "const{spawn}=require('node:child_process');const fs=require('node:fs');const dir=process.argv[1];fs.mkdirSync(dir,{recursive:true});const p=spawn('/bin/sh',['-c',process.argv[2]],{cwd:dir,env:{PATH:'/usr/local/bin:/usr/bin:/bin',HOME:'/tmp',LANG:'C.UTF-8',GIT_CONFIG_NOSYSTEM:'1',GIT_CONFIG_GLOBAL:'/dev/null',GIT_TERMINAL_PROMPT:'0'},detached:true,stdio:'inherit'});const t=setTimeout(()=>{try{process.kill(-p.pid,'SIGKILL')}catch{};process.exit(124)},60000);p.on('error',e=>{console.error(e.message);process.exit(127)});p.on('exit',c=>{clearTimeout(t);process.exit(c??1)});";
            var alias = Path.GetFileName(workspace.WorkRoots[0]);
            var result = await RunAsync(executable, ["exec", "--user=1000:1000", name, "node", "-e", supervisor, "/workspace/" + alias, command], null, 100000, token, allowFailure: true);
            // Export only regular source files. Validate archive names/types/size before touching host files.
            // No .git or dependency tree is retained. Background processes are destroyed below.
            var output = await RunAsync(executable, ["exec", "--user=1000:1000", name, "tar", "--exclude=.git", "--exclude=node_modules", "--exclude=bin", "--exclude=obj", "--exclude=dist", "--exclude=build", "-cf", "-", "-C", "/workspace", "."], null, (int)SandboxWorkspace.MaxBytes + 16 * 1024 * 1024, token);
            await RunAsync(executable, ["rm", "--force", name], null, 10000, CancellationToken.None);
            await workspace.ImportAsync(new MemoryStream(output.Bytes), token);
            return $"Sandbox Linux · exit {result.ExitCode}\n" + Encoding.UTF8.GetString(result.Bytes) + result.Error;
        }
        finally
        {
            // An independent bounded cleanup token still runs after cancellation or timeout.
            try { await RunAsync(executable, ["rm", "--force", name], null, 10000, CancellationToken.None); }
            catch { /* PID 1's fixed 120s lifetime also destroys abandoned containers (--rm). */ }
        }
    }
    sealed record Result(int ExitCode, byte[] Bytes, string Error);
    static async Task<Result> RunAsync(string executable, IReadOnlyList<string> arguments, byte[]? input, int maxOutput, CancellationToken ct, bool allowFailure = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(arguments.Contains("--force") ? 10 : 85));
        var token = timeout.Token;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        // CLI needs its host engine configuration. None of this environment is passed to the container.
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Cannot start container engine.");
        using var cancel = token.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
        async Task<byte[]> ReadAsync(Stream stream, int limit, bool truncate)
        {
            using var memory = new MemoryStream(); var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0)
            {
                var remaining = limit - (int)memory.Length;
                if (count > remaining && !truncate) { timeout.Cancel(); throw new IOException("Sandbox output limit exceeded."); }
                if (remaining > 0) memory.Write(buffer, 0, Math.Min(count, remaining));
            }
            return memory.ToArray();
        }
        var stdout = ReadAsync(process.StandardOutput.BaseStream, maxOutput, allowFailure);
        var stderr = ReadAsync(process.StandardError.BaseStream, 10000, true);
        async Task FeedAsync()
        {
            try { if (input != null) await process.StandardInput.BaseStream.WriteAsync(input, token); }
            finally { process.StandardInput.Close(); }
        }
        try { await Task.WhenAll(stdout, stderr, FeedAsync(), process.WaitForExitAsync(token)); }
        catch { try { process.Kill(entireProcessTree: true); } catch { } throw; }
        token.ThrowIfCancellationRequested();
        var error = Encoding.UTF8.GetString(await stderr);
        if (process.ExitCode != 0 && !allowFailure) throw new IOException(error.Length > 0 ? error : "Container engine exit " + process.ExitCode);
        return new(process.ExitCode, await stdout, error);
    }
}

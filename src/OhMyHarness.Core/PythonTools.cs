using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class PythonTools
{
    static readonly SemaphoreSlim WriteGate = new(1, 1);
    public static bool Handles(string name) => name is "python_info" or "write_python_script" or "run_python_script";
    public static string ScriptsDirectory(int chatId) => Path.Combine(PortableStorage.Root, "scripts", "python", "chat-" + chatId);
    public static string ScriptPath(int chatId, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || !name.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Nom relatif de script .py requis.");
        var path = new SourceAccess(ScriptsDirectory(chatId)).Resolve(name);
        SandboxWorkspace.AssertNoLinks(path);
        return path;
    }
    public static void AddDefinitions(JsonArray definitions, string skills)
    {
        if (!Skills.Enabled(skills, "python")) return;
        JsonObject Text() => new() { ["type"] = "string" };
        void Add(string name, string description, JsonObject properties, params string[] required) => definitions.Add(new JsonObject
        {
            ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties,
                    ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["additionalProperties"] = false } }
        });
        Add("python_info", "Describe the bundled Python runtime and list this conversation's scripts. Read-only; does not install or execute Python.", []);
        Add("write_python_script", "Create or replace a UTF-8 Python script after approval. Stored next to the app, scoped to this conversation. path is relative and ends in .py. Does not execute it; maximum 100000 characters.", new() { ["path"] = Text(), ["code"] = Text() }, "path", "code");
        Add("run_python_script", "Run an existing conversation script using bundled Python after approval. Never uses system Python. args are literal strings, not shell syntax. working_directory defaults to the first attached source folder or the conversation scripts folder; optional directory must be within attached sources. Returns exit_code, stdout, stderr and timeout status. Local execution has user's privileges, NOT a security sandbox. No stdin interaction. Read outputs and fix errors. timeout_seconds defaults to 30, max 600.",
            new() { ["path"] = Text(), ["args"] = new JsonObject { ["type"] = "array", ["items"] = Text() }, ["working_directory"] = Text(), ["timeout_seconds"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 600, ["default"] = 30 } }, "path");
    }
    public static async Task<string> CallAsync(ConversationSession run, string name, JsonObject args, Func<string> skills,
        Func<string, string, string, CancellationToken, Task<bool>> approve, CancellationToken ct)
    {
        void Check()
        {
            AgentPolicy.Demand(run.Chat.ExecutionMode, name); SandboxWorkspace.Demand(run.Chat.SandboxEnabled, name);
            if (!Skills.Enabled(skills(), "python")) throw new UnauthorizedAccessException("Skill Script Python désactivé.");
            ct.ThrowIfCancellationRequested();
        }
        Check();
        var scripts = ScriptsDirectory(run.Chat.Id);
        SandboxWorkspace.AssertNoLinks(scripts);
        if (name == "python_info")
        {
            var bundle = PythonRuntime.Bundle();
            return JsonSerializer.Serialize(new { bundle.Version, bundle.Rid, executable = PythonRuntime.Executable(bundle), extracted = File.Exists(PythonRuntime.Executable(bundle)),
                scripts_directory = scripts, scripts = Directory.Exists(scripts) ? Directory.EnumerateFiles(scripts, "*.py", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(200).Select(p => Path.GetRelativePath(scripts, p)).ToArray() : [],
                execution = "Local, user privileges; Plan forbids writes/execution; unavailable in container sandbox." });
        }
        var path = ScriptPath(run.Chat.Id, args["path"]?.GetValue<string>() ?? "");
        if (name == "write_python_script")
        {
            var code = args["code"]?.GetValue<string>() ?? "";
            if (code.Length is 0 or > 100000) throw new ArgumentException("Code requis : 1 à 100 000 caractères.");
            if (!await approve("python|write|" + scripts, "Écrire un script Python / Write Python script", path + "\n\n" + code, ct)) return "Accès refusé / Access denied.";
            Check(); path = ScriptPath(run.Chat.Id, args["path"]!.GetValue<string>());
            await WriteGate.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".write-" + Guid.NewGuid().ToString("N");
                try { await File.WriteAllTextAsync(temp, code, new UTF8Encoding(false), ct); File.Move(temp, path, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            finally { WriteGate.Release(); }
            return JsonSerializer.Serialize(new { written = path, characters = code.Length });
        }
        if (name != "run_python_script") throw new ArgumentException("Unknown Python tool.");
        if (new FileInfo(path).Length > 400000) throw new IOException("Script trop volumineux.");
        var content = await File.ReadAllTextAsync(path, ct);
        var timeout = TerminalHub.ValidateTimeout(args["timeout_seconds"]?.GetValue<int>() ?? 30);
        var arguments = (args["args"] as JsonArray)?.Select(a => a?.GetValue<string>() ?? "").ToArray() ?? [];
        if (arguments.Length > 100 || arguments.Sum(a => a.Length) > 32000 || arguments.Any(a => a.Contains('\0'))) throw new ArgumentException("Arguments invalides ou trop longs.");
        var requested = args["working_directory"]?.GetValue<string>();
        var directory = requested == null ? run.Project.GetSourceFolders().FirstOrDefault(Directory.Exists) ?? scripts : new SourceAccess(run.Project.GetSourceFolders()).Resolve(requested);
        SandboxWorkspace.AssertNoLinks(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        if (!await approve("python|execute|" + scripts + "|" + directory, "Exécuter un script Python / Run Python script",
            path + "\nDossier : " + directory + "\nArguments : " + JsonSerializer.Serialize(arguments) + "\nDélai : " + timeout + " s\nExécution locale avec vos droits.\n\n" + content, ct)) return "Accès refusé / Access denied.";
        Check();
        var executable = await PythonRuntime.EnsureAsync(ct);
        // Execute exactly the code shown in the permission dialog, even if the file changes meanwhile.
        return await ExecuteAsync(executable, path, content, arguments, directory, Path.Combine(PortableStorage.Temporary, "python", "chat-" + run.Chat.Id), timeout, ct);
    }
    public static async Task<string> ExecuteAsync(string executable, string scriptPath, string code, string[] args, string directory, string temporary, int timeoutSeconds, CancellationToken ct)
    {
        TerminalHub.ValidateTimeout(timeoutSeconds); SandboxWorkspace.AssertNoLinks(temporary); Directory.CreateDirectory(temporary);
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "-I", "-B", "-u", "-X", "utf8", "-c",
            "import sys,os,types; p=sys.argv[1]; sys.argv=sys.argv[1:]; sys.path[:0]=[os.path.dirname(p),os.getcwd()]; code=sys.stdin.read(); m=types.ModuleType('__main__'); m.__file__=p; m.__package__=None; sys.modules['__main__']=m; exec(compile(code,p,'exec'),m.__dict__)", scriptPath }.Concat(args)) start.ArgumentList.Add(argument);
        start.Environment["TMP"] = temporary; start.Environment["TEMP"] = temporary; start.Environment["TMPDIR"] = temporary;
        start.Environment["PIP_CACHE_DIR"] = Path.Combine(temporary, "pip-cache");
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        ct.ThrowIfCancellationRequested(); process.Start();
        var stdoutText = new StringBuilder(); var stderrText = new StringBuilder();
        bool stdoutTruncated = false, stderrTruncated = false;
        async Task Read(StreamReader reader, StringBuilder output, Action truncated)
        {
            var buffer = new char[4096]; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), deadline.Token)) > 0)
            {
                var take = Math.Min(count, 100000 - output.Length); if (take < count) truncated();
                output.Append(buffer, 0, take);
            }
        }
        var stdout = Read(process.StandardOutput, stdoutText, () => stdoutTruncated = true);
        var stderr = Read(process.StandardError, stderrText, () => stderrTruncated = true);
        bool timedOut = false;
        try
        {
            await process.StandardInput.WriteAsync(code.AsMemory(), deadline.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token); await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { timedOut = true; }
        finally
        {
            if (!process.HasExited) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
        ct.ThrowIfCancellationRequested();
        return JsonSerializer.Serialize(new { exit_code = process.HasExited ? (int?)process.ExitCode : null, timed_out = timedOut,
            stdout = stdoutText.ToString() + (stdoutTruncated ? "\n[output truncated]" : ""), stderr = stderrText.ToString() + (stderrTruncated ? "\n[output truncated]" : ""), script = scriptPath, working_directory = directory });
    }
}

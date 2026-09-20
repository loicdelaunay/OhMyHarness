using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

/// <summary>Named, conversation-scoped command terminals; one job per tab, many tabs may run concurrently.</summary>
public sealed class TerminalHub : IDisposable
{
    public record View(string Id, int ChatId, bool Sandbox, string Name, string Directory, string Shell, string Status, string? JobId, string Command, string Output);
    sealed class Job(string command, CancellationToken ct)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Command { get; } = command;
        public string Status { get; set; } = "running";
        public StringBuilder Output { get; } = new();
        public CancellationTokenSource Cancel { get; } = CancellationTokenSource.CreateLinkedTokenSource(ct);
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    sealed class Terminal(int chatId, bool sandbox, string name, string directory)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public int ChatId { get; } = chatId;
        public bool Sandbox { get; } = sandbox;
        public string Name { get; } = name;
        public string Directory { get; } = directory;
        public List<Job> Jobs { get; } = [];
        public bool Closing { get; set; }
    }
    readonly object sync = new();
    readonly Dictionary<string, Terminal> terminals = [];
    bool disposed;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
    Terminal Find(int chatId, bool? sandbox, string id)
    {
        if (!terminals.TryGetValue(id, out var terminal) || terminal.ChatId != chatId || sandbox.HasValue && sandbox != terminal.Sandbox || terminal.Closing)
            throw new UnauthorizedAccessException("Terminal inconnu ou hors de cette conversation / Unknown terminal in this conversation.");
        return terminal;
    }
    static View Snapshot(Terminal t, Job? job = null)
    {
        job ??= t.Jobs.LastOrDefault();
        return new(t.Id, t.ChatId, t.Sandbox, t.Name, t.Directory, t.Sandbox ? "Linux sh (sandbox)" : PlatformSupport.ShellName,
            job?.Status ?? "idle", job?.Id, job?.Command ?? "", job?.Output.ToString() ?? "");
    }
    public List<View> List(int chatId, bool? sandbox = null)
    { lock (sync) return terminals.Values.Where(t => t.ChatId == chatId && (!sandbox.HasValue || t.Sandbox == sandbox) && !t.Closing).Select(t => Snapshot(t)).ToList(); }
    public View Create(int chatId, bool sandbox, string name, string directory)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (terminals.Count >= 64 || terminals.Values.Count(t => t.ChatId == chatId) >= 12) throw new InvalidOperationException("Limite : 12 terminaux par conversation, 64 au total.");
            if (name.Length > 80) throw new ArgumentException("Nom trop long (80 caractères).");
            var t = new Terminal(chatId, sandbox, string.IsNullOrWhiteSpace(name) ? "Terminal " + (List(chatId).Count + 1) : name.Trim(), Path.GetFullPath(directory));
            terminals.Add(t.Id, t); return Snapshot(t);
        }
    }
    public View Read(int chatId, bool? sandbox, string id, string? jobId = null)
    {
        lock (sync)
        {
            var t = Find(chatId, sandbox, id);
            var job = jobId == null ? t.Jobs.LastOrDefault() : t.Jobs.FirstOrDefault(j => j.Id == jobId) ?? throw new ArgumentException("Job inconnu / Unknown job.");
            return Snapshot(t, job);
        }
    }
    public View Start(int chatId, bool sandbox, string id, string command, Func<string, Action<string>, CancellationToken, Task<string>> execute, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 100000) throw new ArgumentException("Commande requise, maximum 100 000 caractères.");
        ct.ThrowIfCancellationRequested();
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var t = Find(chatId, sandbox, id);
            if (t.Jobs.LastOrDefault()?.Status == "running") throw new InvalidOperationException("Ce terminal travaille déjà. Créez un autre terminal pour exécuter en parallèle.");
            if (terminals.Values.Count(x => x.Jobs.LastOrDefault()?.Status == "running") >= 16) throw new InvalidOperationException("16 commandes simultanées maximum.");
            var job = new Job(command, ct); t.Jobs.Add(job);
            if (t.Jobs.Count > 10) t.Jobs.RemoveAt(0);
            // Execute outside the UI thread and independently of the tool dispatcher.
            _ = Task.Run(async () => {
                void Append(string value) { lock (sync) { var room = 100000 - job.Output.Length; if (room > 0) job.Output.Append(value.AsSpan(0, Math.Min(room, value.Length))); } }
                try
                {
                    var result = await execute(command, Append, job.Cancel.Token);
                    lock (sync) { job.Output.Clear(); job.Output.Append(result.AsSpan(0, Math.Min(100000, result.Length))); job.Status = "completed"; }
                }
                catch (OperationCanceledException) { lock (sync) { job.Status = job.Cancel.IsCancellationRequested ? "cancelled" : "timed_out"; } Append("\n[Arrêt ou délai atteint / Stopped or timed out]"); }
                catch (Exception ex) { lock (sync) job.Status = "failed"; Append("\n" + ex.Message); }
                finally { lock (sync) { job.Cancel.Dispose(); job.Done.TrySetResult(); } }
            });
            return Snapshot(t, job);
        }
    }
    public async Task<View> WaitAsync(int chatId, bool? sandbox, string id, string? jobId, int timeoutMs, CancellationToken ct)
    {
        if (timeoutMs is < 0 or > 30000) throw new ArgumentOutOfRangeException(nameof(timeoutMs), "0..30000 ms");
        Task? completion;
        lock (sync)
        {
            var t = Find(chatId, sandbox, id);
            var job = jobId == null ? t.Jobs.LastOrDefault() : t.Jobs.FirstOrDefault(j => j.Id == jobId) ?? throw new ArgumentException("Unknown job.");
            completion = job?.Done.Task; jobId = job?.Id;
        }
        if (completion != null && timeoutMs > 0)
            try { await completion.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct); } catch (TimeoutException) { }
        return Read(chatId, sandbox, id, jobId);
    }
    public void Stop(int chatId, bool? sandbox, string id)
    { lock (sync) { var job = Find(chatId, sandbox, id).Jobs.LastOrDefault(); if (job?.Status == "running") job.Cancel.Cancel(); } }
    public async Task DeleteAsync(int chatId, bool? sandbox, string id)
    {
        Terminal t; Task? done;
        lock (sync)
        {
            t = Find(chatId, sandbox, id); t.Closing = true;
            var job = t.Jobs.LastOrDefault(); done = job?.Done.Task;
            if (job?.Status == "running") job.Cancel.Cancel();
        }
        if (done != null) await done;
        lock (sync) terminals.Remove(t.Id);
    }
    public async Task StopChatAsync(int chatId, bool? sandbox = null)
    {
        List<Task> pending = [];
        lock (sync)
            foreach (var t in terminals.Values.Where(t => t.ChatId == chatId && (!sandbox.HasValue || t.Sandbox == sandbox)))
                if (t.Jobs.LastOrDefault() is { Status: "running" } job) { job.Cancel.Cancel(); pending.Add(job.Done.Task); }
        await Task.WhenAll(pending);
    }
    public async Task RemoveChatAsync(int chatId)
    {
        await StopChatAsync(chatId);
        lock (sync) foreach (var id in terminals.Values.Where(t => t.ChatId == chatId).Select(t => t.Id).ToList()) terminals.Remove(id);
    }
    public void Dispose()
    { lock (sync) { disposed = true; foreach (var t in terminals.Values) if (t.Jobs.LastOrDefault() is { Status: "running" } j) j.Cancel.Cancel(); } }

    public static bool Handles(string name) => name is "run_terminal" or "list_terminals" or "create_terminal" or "delete_terminal" or "start_terminal" or "read_terminal" or "wait_terminal" or "stop_terminal";
    public static bool IsBoundedWait(string name, string arguments)
    {
        if (name != "wait_terminal") return false;
        try { var milliseconds = JsonNode.Parse(arguments)?["timeout_ms"]?.GetValue<int>() ?? 10000; return milliseconds is >= 1000 and <= 30000; }
        catch { return false; }
    }
    public static void AddDefinitions(JsonArray definitions)
    {
        void Add(string name, string description, JsonObject properties, params string[] required) => definitions.Add(new JsonObject {
            ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description,
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray(required.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()), ["additionalProperties"] = false } } });
        JsonObject Text() => new() { ["type"] = "string" };
        JsonObject Id() => new() { ["terminal_id"] = Text() };
        Add("list_terminals", "List this conversation's terminals, shell, directory and latest job/status. Use read_terminal for output. Does not execute commands.", []);
        Add("create_terminal", "Create a named terminal tab in the project directory. Max 12 per conversation. Commands are noninteractive, fresh shell sessions; cwd and environment changes are not retained between commands.", new() { ["name"] = Text() });
        Add("delete_terminal", "Stop the terminal's process tree and close its tab. Output is discarded.", Id(), "terminal_id");
        var start = Id(); start["command"] = Text();
        Add("start_terminal", "Request approval, then launch a command and return immediately with jobId. Run commands in different terminals in parallel, then use read_terminal/wait_terminal. One active command per terminal, 60 second command limit. Output is bounded. completed means process ended; inspect the reported exit code.", start, "terminal_id", "command");
        var read = Id(); read["job_id"] = Text();
        Add("read_terminal", "Read status and current output without waiting. Optional job_id selects one of the last 10 jobs. Never rerun a command just to check progress.", read, "terminal_id");
        var wait = Id(); wait["job_id"] = Text(); wait["timeout_ms"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 30000 };
        Add("wait_terminal", "Asynchronously wait up to timeout_ms (default 10000, maximum 30000) and return status/output. If still running, do other work or wait again; other terminals continue independently.", wait, "terminal_id");
        Add("stop_terminal", "Cancel the active command and its child processes, retaining the tab and output.", Id(), "terminal_id");
    }
    public async Task<string> CallAsync(ConversationSession run, string name, JsonObject args, Func<string> skills,
        Func<string, string, string, CancellationToken, Task<bool>> approve, CancellationToken ct)
    {
        void Check() { AgentPolicy.Demand(run.Chat.ExecutionMode, name); SandboxWorkspace.Demand(run.Chat.SandboxEnabled, name); if (!Skills.Enabled(skills(), "terminal")) throw new UnauthorizedAccessException("Skill Terminal désactivé."); }
        Check();
        int chat = run.Chat.Id; bool sandbox = run.Chat.SandboxEnabled;
        string id = args["terminal_id"]?.GetValue<string>() ?? "";
        switch (name)
        {
            case "run_terminal":
                var directory = run.Project.GetSourceFolders().FirstOrDefault(Directory.Exists) ?? throw new InvalidOperationException("Associez un dossier source.");
                var legacy = List(chat, sandbox).FirstOrDefault(t => t.Name == "Terminal" && t.Status != "running" && PlatformSupport.PathComparer.Equals(t.Directory, directory)) ?? Create(chat, sandbox, "Terminal", directory);
                var started = await CallAsync(run, "start_terminal", new JsonObject { ["terminal_id"] = legacy.Id, ["command"] = args["command"]?.GetValue<string>() ?? "" }, skills, approve, ct);
                if (!started.StartsWith('{')) return started;
                var current = Read(chat, sandbox, legacy.Id);
                while (current.Status == "running") current = await WaitAsync(chat, sandbox, legacy.Id, current.JobId, 30000, ct);
                return current.Output;
            case "list_terminals": return Serialize(List(chat, sandbox).Select(t => new { t.Id, t.Name, t.Shell, t.Directory, t.Status, t.JobId, t.Sandbox }));
            case "create_terminal": return Serialize(Create(chat, sandbox, args["name"]?.GetValue<string>() ?? "", run.Project.GetSourceFolders().FirstOrDefault(Directory.Exists) ?? throw new InvalidOperationException("Associez un dossier source.")));
            case "delete_terminal": await DeleteAsync(chat, sandbox, id); return "Terminal fermé / Terminal closed.";
            case "stop_terminal": Stop(chat, sandbox, id); return Serialize(Read(chat, sandbox, id));
            case "read_terminal": return Serialize(Read(chat, sandbox, id, args["job_id"]?.GetValue<string>()));
            case "wait_terminal": return Serialize(await WaitAsync(chat, sandbox, id, args["job_id"]?.GetValue<string>(), args["timeout_ms"]?.GetValue<int>() ?? 10000, ct));
            case "start_terminal":
                var terminal = Read(chat, sandbox, id);
                var command = args["command"]?.GetValue<string>() ?? "";
                // A terminal created on a previously linked root cannot silently retain access after it is detached.
                if (!run.Project.GetSourceFolders().Any(root => PlatformSupport.PathComparer.Equals(Path.GetFullPath(root), terminal.Directory))) throw new UnauthorizedAccessException("Le dossier de ce terminal n'est plus associé au projet.");
                if (!await approve((sandbox ? "sandbox-terminal|" : "terminal|") + terminal.Directory, run.Chat.Title + " · " + terminal.Name, terminal.Directory + "\n\n" + command, ct)) return "Accès refusé / Access denied.";
                Check(); ct.ThrowIfCancellationRequested();
                return Serialize(Start(chat, sandbox, id, command, (cmd, output, token) => sandbox
                    ? SandboxContainer.ExecuteIsolatedAsync(run.Sandbox ?? throw new InvalidOperationException("Sandbox inactive."), run.SandboxEngine!, cmd, token)
                    : WorkspaceTools.ShellAsync(cmd, terminal.Directory, token, output), ct));
            default: throw new ArgumentException("Unknown terminal tool.");
        }
    }
}

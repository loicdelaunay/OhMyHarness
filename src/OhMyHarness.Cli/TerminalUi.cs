using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using OhMyHarness.Core;
using OhMyHarness.Core.Hosting;

namespace OhMyHarness.Cli;

public sealed record Choice(string Value, string Label, string Detail = "");
sealed class UiDialog(string title, string body, List<Choice>? choices, bool secret = false)
{
    public string Title = title, Body = body;
    public List<Choice>? Choices = choices;
    public bool Secret = secret;
    public bool PreviewTheme;
    public InputBuffer Input = new();
    public int Selected, Scroll;
    public TaskCompletionSource<string?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<Choice> Filtered => Choices?.Where(c => (c.Label + " " + c.Detail).Contains(Input.Text, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
    public string? ThemePreview
    {
        get
        {
            var choices = Filtered;
            if (!PreviewTheme || Completion.Task.IsCompleted || choices.Count == 0) return null;
            return choices[Math.Clamp(Selected, 0, choices.Count - 1)].Value;
        }
    }
}
sealed class ChatView
{
    public SortedDictionary<int, Message> Messages = [];
    public Dictionary<int, string> Reasoning = [];
    public Dictionary<string, SubagentRecord> Children = [];
    public JsonArray Inbox = [];
    public string Status = "", Notice = "";
    public int Tokens, Limit;
    public double Speed;
    public int Scroll;
    public int LastLineCount, LastWidth;
    public bool Loading;
}

public sealed partial class TerminalUi(CliOptions options) : IDisposable
{
    readonly ConcurrentQueue<Action> actions = new();
    readonly ConcurrentQueue<UiDialog> dialogs = new();
    readonly CancellationTokenSource lifetime = new();
    readonly InputBuffer editor = new();
    readonly CommandCompletion completion = new(Commands);
    readonly Dictionary<int, ChatView> views = [];
    readonly Dictionary<int, Task> runs = [];
    readonly HashSet<int> halted = [];
    readonly Dictionary<int, List<Attachment>> attachments = [];
    readonly Dictionary<int, string> drafts = [];
    readonly Dictionary<int, (int ChatId, int ItemId)> queueRows = [];
    int queueButtonsStart;
    readonly List<Task> operations = [];
    CliClient client = null!;
    WorkspaceSnapshot? workspace;
    UiDialog? dialog;
    int chatId, providerId, pendingOperations, frame;
    bool quit, dirty = true, showDetails;
    string notice = "Chargement de votre espace…", escape = "";
    readonly TerminalPaste pasted = new();
    bool pasting;
    DateTime escapeAt;
    readonly ConcurrentDictionary<string, byte> activeQuestions = new();
    readonly ConcurrentDictionary<string, CancellationTokenSource> questionCancellation = new();
    TerminalSession? terminal;
    ITerminalInput? terminalInput;
    char highSurrogate;
    string L(string fr, string en) => workspace?.State.Language == "en" ? en : fr;
    ChatView View(int id) => views.TryGetValue(id, out var v) ? v : views[id] = new();
    Chat? CurrentChat => workspace?.Chats.FirstOrDefault(c => c.Id == chatId);
    Project? CurrentProject => workspace?.Projects.FirstOrDefault(p => p.Id == CurrentChat?.ProjectId);
    Provider? CurrentProvider => workspace?.Providers.FirstOrDefault(p => p.Id == providerId);
    bool Running(int id) => runs.TryGetValue(id, out var task) && !task.IsCompleted;
    void Post(Action action) => actions.Enqueue(action);
    void Work(Func<Task> operation)
    {
        pendingOperations++;
        operations.Add(Task.Run(async () =>
        {
            try { await operation(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLog.Write(AppLogLevel.Error, "cli.action_failed", ex); Post(() => { notice = "! " + ex.Message; View(chatId).Notice = notice; }); }
            finally { Post(() => pendingOperations--); }
        }));
    }
    async Task Refresh()
    {
        var snapshot = await client.Snapshot(lifetime.Token);
        Post(() => workspace = snapshot);
    }
    public async Task<int> RunAsync()
    {
        terminal = new();
        terminalInput = OperatingSystem.IsWindows() ? new WindowsTerminalInput() : new MacTerminalInput();
        client = new(options, Host, Emit);
        Work(async () =>
        {
            var initial = await client.Initialize(options, lifetime.Token);
            Post(() => { workspace = initial.Snapshot; providerId = initial.ProviderId; notice = L("Prêt · / pour les commandes", "Ready · / for commands"); SwitchChat(initial.ChatId); });
            if (FeatureSettings.Read(initial.Snapshot.State.FeaturesJson).CliCheckUpdates) await CheckStartupUpdate();
        });
        int previousWidth = 0, previousHeight = 0;
        try
        {
            while (!quit)
            {
                while (actions.TryDequeue(out var action)) { action(); dirty = true; }
                if (dialog?.Completion.Task.IsCompleted == true) { dialog = null; dirty = true; }
                if (dialog == null && dialogs.TryDequeue(out var next)) { dialog = next; dirty = true; }
                while (terminalInput.TryRead(out var character)) { ReadKey(TerminalKeys.Character(character)); dirty = true; }
                if (escape.Length > 0 && (DateTime.UtcNow - escapeAt).TotalMilliseconds > 100)
                { escape = ""; Key(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false)); dirty = true; }
                int width = Math.Clamp(Console.WindowWidth - 1, 1, 240), height = Math.Clamp(Console.WindowHeight, 1, 100);
                if (width != previousWidth || height != previousHeight) { previousWidth = width; previousHeight = height; dirty = true; }
                if (dirty || dialog == null && frame++ % 4 == 0 && (pendingOperations > 0 || runs.Values.Any(t => !t.IsCompleted)))
                { terminal.Paint(Draw(width, height)); dirty = false; }
                operations.RemoveAll(t => t.IsCompleted);
                await Task.Delay(40);
            }
        }
        finally
        {
            lifetime.Cancel(); client.Service.CancelAll();
            foreach (var prompt in dialogs) prompt.Completion.TrySetCanceled(); dialog?.Completion.TrySetCanceled();
            try { await Task.WhenAll(operations.Concat(runs.Values)).WaitAsync(TimeSpan.FromSeconds(8)); } catch { }
            await client.DisposeAsync(); terminalInput?.Dispose(); terminalInput = null; terminal.Dispose(); terminal = null;
        }
        return 0;
    }
    public void Dispose() { lifetime.Cancel(); terminalInput?.Dispose(); terminalInput = null; terminal?.Dispose(); lifetime.Dispose(); }

    async Task<string?> Prompt(string title, string body = "", List<Choice>? choices = null, bool secret = false, string initial = "", CancellationToken ct = default, bool previewTheme = false, string? selectedValue = null)
    {
        var request = new UiDialog(title, body, choices, secret) { PreviewTheme = previewTheme };
        request.Input.Set(initial);
        if (selectedValue != null) request.Selected = Math.Max(0, request.Filtered.FindIndex(c => c.Value == selectedValue));
        dialogs.Enqueue(request);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct);
        using var registration = linked.Token.Register(() => request.Completion.TrySetCanceled(linked.Token));
        return await request.Completion.Task;
    }
    async Task<JsonNode?> Host(string name, JsonObject p, CancellationToken ct)
    {
        if (name == "permission")
        {
            var result = await Prompt(S(p, "title"), S(p, "details"),
                [new("deny", L("Refuser", "Deny")), new("allow", L("Autoriser une fois", "Allow once")), new("always", L("Toujours autoriser cet accès", "Always allow this access"))], ct: ct);
            return JsonValue.Create(result ?? "deny");
        }
        throw new NotSupportedException(name + L(" : utilisez l’application graphique ou un serveur MCP configuré avec /mcp.", ": use the desktop app or an MCP server configured with /mcp."));
    }
    Task Emit(object value)
    {
        var data = CliClient.J(value); Post(() => OnEvent(data)); return Task.CompletedTask;
    }
    static string S(JsonNode? node, string key) => node?[key]?.GetValue<string>() ?? "";
    static int I(JsonNode? node, string key) => node?[key]?.GetValue<int>() ?? 0;
    void OnEvent(JsonObject data)
    {
        int id = I(data, "chatId"); var view = View(id);
        switch (S(data, "event"))
        {
            case "chat.renamed": Work(Refresh); break;
            case "started": view.Status = L("Le modèle réfléchit…", "Thinking…"); view.Notice = ""; break;
            case "status": view.Status = S(data, "text"); break;
            case "stream":
                int messageId = I(data, "messageId");
                view.Messages[messageId] = new() { Id = messageId, ChatId = id, Role = "assistant", Content = S(data, "text"), State = "streaming" };
                view.Reasoning[messageId] = S(data, "reasoning"); view.Tokens = I(data, "tokens"); view.Limit = I(data, "limit");
                view.Speed = data["speed"]?.GetValue<double>() ?? 0; break;
            case "message":
                var item = data["message"]!.Deserialize<Message>(HarnessService.Json)!;
                view.Messages[item.Id] = item;
                if (data["message"]?["reasoning"] is { } reasoning) view.Reasoning[item.Id] = reasoning.GetValue<string>();
                if (data["title"] is { } title && workspace?.Chats.FirstOrDefault(c => c.Id == id) is { } chat) chat.Title = title.GetValue<string>();
                break;
            case "subagent": var child = data["child"]!.Deserialize<SubagentRecord>(HarnessService.Json)!; view.Children[child.Id] = child; break;
            case "inbox": view.Inbox = data["items"]?.AsArray() ?? []; break;
            case "done": view.Status = S(data, "error") is { Length: > 0 } error ? error : S(data, "status"); break;
            case "question":
                activeQuestions.TryAdd(S(data, "id"), 0);
                var questionToken = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var token = questionToken.Token;
                questionCancellation[S(data, "id")] = questionToken;
                Work(() => AnswerQuestions(data, token)); break;
            case "question.closed":
                activeQuestions.TryRemove(S(data, "id"), out _);
                if (questionCancellation.TryRemove(S(data, "id"), out var pending)) { pending.Cancel(); pending.Dispose(); }
                break;
        }
    }
    async Task AnswerQuestions(JsonObject data, CancellationToken ct)
    {
        var questions = data["questions"]!.Deserialize<List<AgentQuestion>>(HarnessService.Json)!;
        var answers = new List<List<string>>(); bool cancelled = false;
        foreach (var question in questions)
        {
            var values = new List<string>();
            while (true)
            {
                List<Choice>? choices = question.Options.Count == 0 ? null : question.Options.Select((o, i) => new Choice(i.ToString(), (values.Contains(o.Label) ? "[x] " : "") + o.Label, o.Description)).ToList();
                if (choices != null && question.Custom) choices.Add(new("custom", L("Réponse libre…", "Custom answer…")));
                if (choices != null && question.Multiple && values.Count > 0) choices.Add(new("done", L("Valider la sélection", "Submit selection")));
                string? result = await Prompt($"{L("Question", "Question")} · #{I(data, "chatId")}", question.Question, choices, ct: ct);
                if (!activeQuestions.ContainsKey(S(data, "id"))) return;
                if (result == null) { cancelled = true; break; }
                if (result == "done" && choices != null) break;
                string? value = choices == null ? result : result == "custom" ? await Prompt(question.Question, ct: ct) : question.Options[int.Parse(result)].Label;
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (values.Contains(value)) values.Remove(value); else values.Add(value);
                if (!question.Multiple || choices == null) break;
            }
            answers.Add(values); if (cancelled) break;
        }
        if (activeQuestions.ContainsKey(S(data, "id"))) await client.Call("question.answer", new { id = S(data, "id"), chatId = I(data, "chatId"), cancelled, answers }, lifetime.Token);
    }
    void SwitchChat(int id)
    {
        if (chatId != 0) drafts[chatId] = editor.Text;
        chatId = id; editor.Set(drafts.GetValueOrDefault(id, "")); var view = View(id); view.Loading = true;
        Work(async () =>
        {
            var history = await client.History(id, lifetime.Token);
            var inbox = (await client.Call("inbox.list", new { chatId = id }, lifetime.Token))!.AsArray();
            Post(() =>
            {
                foreach (var item in history) if (!view.Messages.ContainsKey(item.Id))
                {
                    view.Messages[item.Id] = item;
                    try { view.Reasoning[item.Id] = S(JsonNode.Parse(string.IsNullOrWhiteSpace(item.WireJson) ? "{}" : item.WireJson), "reasoning_content"); } catch (JsonException) { }
                }
                view.Inbox = inbox; view.Loading = false;
            });
        });
    }
    void StartRun(int id, Func<Task> action)
    {
        runs[id] = Task.Run(async () =>
        {
            try { await action(); }
            catch (OperationCanceledException) { Post(() => View(id).Status = L("Arrêté", "Stopped")); }
            catch (Exception ex) { Post(() => View(id).Status = "! " + ex.Message); }
            finally { await Refresh(); Post(() => dirty = true); }
        });
    }
    void Send(bool steer = false)
    {
        if (workspace == null || CurrentChat == null) return;
        string text = editor.Text.Trim();
        if (text.StartsWith('/') && !steer) { editor.Set(""); Command(text); return; }
        var images = attachments.GetValueOrDefault(chatId, []);
        if (text.Length == 0 && images.Count == 0) return;
        if (CurrentProvider == null || string.IsNullOrWhiteSpace(CurrentProvider.Model)) { notice = L("Configurez un fournisseur avec /connect.", "Configure a provider with /connect."); return; }
        int id = chatId, selectedProvider = providerId;
        var args = new { chatId = id, providerId = selectedProvider, text,
            images = images.Select(a => new { a.Name, a.Mime, data = Convert.ToBase64String(a.Data) }).ToArray(), mode = steer ? "steering" : "queued" };
        editor.Set(""); drafts[id] = ""; attachments[id] = []; View(id).Scroll = 0;
        if (Running(id))
        {
            var previous = runs[id];
            Work(async () =>
            {
                var result = await client.Call("inbox.add", args, lifetime.Token);
                // Cover the race where the current turn finishes while the input is being queued.
                await previous;
                if (result?["autoStart"]?.GetValue<bool>() == true)
                    Post(() => { if (!halted.Contains(id) && !Running(id) && !lifetime.IsCancellationRequested) StartRun(id, async () => { await client.Call("inbox.resume", new { chatId = id }, lifetime.Token); }); });
            });
        }
        else { halted.Remove(id); StartRun(id, async () => { await client.Call("send", args, lifetime.Token); }); }
    }
    void ReadKey(ConsoleKeyInfo key)
    {
        if (pasting)
        {
            if (pasted.Feed(key.KeyChar))
            {
                if (pasted.TooLong) notice = L("Collage trop volumineux (128 000 caractères maximum).", "Paste too large (128,000 characters maximum).");
                (dialog?.Input ?? editor).Insert(pasted.Take()); pasting = false;
            }
            return;
        }
        if (escape.Length > 0)
        {
            escape += key.KeyChar;
            escapeAt = DateTime.UtcNow;
            if (escape == "\x1b[200~") { escape = ""; pasting = true; return; }
            if (TerminalKeys.Sequence(escape) is { } decoded) { escape = ""; Key(decoded); return; }
            if (escape.Length == 2 && escape[1] is not ('[' or 'O'))
            {
                escape = ""; Key(new ConsoleKeyInfo(key.KeyChar, key.Key, false, true, key.Modifiers.HasFlag(ConsoleModifiers.Control))); return;
            }
            if (escape.StartsWith("\x1b[<", StringComparison.Ordinal) && escape[^1] is 'm' or 'M')
            {
                var fields = escape[3..^1].Split(';');
                if (fields.Length == 3 && int.TryParse(fields[0], out int button) && int.TryParse(fields[1], out int x) && int.TryParse(fields[2], out int y))
                {
                    if (button is 64 or 65) { if (dialog != null) dialog.Scroll = Math.Max(0, dialog.Scroll + (button == 64 ? -3 : 3)); else View(chatId).Scroll = Math.Max(0, View(chatId).Scroll + (button == 64 ? 3 : -3)); }
                    else if (button == 0 && escape[^1] == 'M' && dialog == null && queueRows.TryGetValue(y - 1, out var queued) && x - 1 >= queueButtonsStart && x - 1 < queueButtonsStart + 12)
                    {
                        var action = ((x - 1 - queueButtonsStart) / 4) switch { 0 => "edit", 1 => "delete", _ => "steer" };
                        Work(() => QueueAction(queued.ChatId, queued.ItemId, action));
                    }
                }
                escape = "";
            }
            else if (escape.Length > 40 || escape.Length > 2 && escape[^1] is >= '@' and <= '~') escape = "";
            return;
        }
        if (key.KeyChar == '\x1b') { escape = "\x1b"; escapeAt = DateTime.UtcNow; return; }
        if (char.IsHighSurrogate(key.KeyChar)) { highSurrogate = key.KeyChar; return; }
        if (highSurrogate != '\0')
        {
            var high = highSurrogate; highSurrogate = '\0';
            if (char.IsLowSurrogate(key.KeyChar)) { (dialog?.Input ?? editor).Insert(new string([high, key.KeyChar])); return; }
        }
        Key(key);
    }
    void Key(ConsoleKeyInfo key)
    {
        bool ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        if (ctrl && key.Key == ConsoleKey.Q) { RequestQuit(); return; }
        if (InputShortcuts.HandleClipboard(dialog?.Input ?? editor, key, dialog?.Secret == true,
            TerminalClipboard.Copy, TerminalClipboard.Paste,
            () => notice = L("Presse-papiers indisponible ou texte trop long. Réessayez.", "Clipboard unavailable or text too long. Try again."))) return;
        if (dialog != null)
        {
            if (key.Key == ConsoleKey.Escape) dialog.Completion.TrySetResult(null);
            else if (key.Key == ConsoleKey.PageUp) dialog.Scroll = Math.Max(0, dialog.Scroll - 6);
            else if (key.Key == ConsoleKey.PageDown) dialog.Scroll += 6;
            else if (key.Key == ConsoleKey.UpArrow && key.Modifiers == 0 && dialog.Choices != null) dialog.Selected = Math.Max(0, dialog.Selected - 1);
            else if (key.Key == ConsoleKey.DownArrow && key.Modifiers == 0 && dialog.Choices != null) dialog.Selected = Math.Min(dialog.Filtered.Count - 1, dialog.Selected + 1);
            else if (key.Key == ConsoleKey.Enter)
            {
                if (dialog.Choices == null) dialog.Completion.TrySetResult(dialog.Input.Text);
                else if (dialog.Filtered.Count > 0) dialog.Completion.TrySetResult(dialog.Filtered[Math.Clamp(dialog.Selected, 0, dialog.Filtered.Count - 1)].Value);
            }
            else { dialog.Input.Key(key); dialog.Selected = 0; }
            return;
        }
        if (ctrl)
        {
            switch (key.Key)
            {
                case ConsoleKey.P: Command("/commands"); return;
                case ConsoleKey.N: Command("/new"); return;
                case ConsoleKey.O: Command("/chats"); return;
                case ConsoleKey.B: Command("/models"); return;
                case ConsoleKey.L: terminal?.Paint(Draw(Math.Clamp(Console.WindowWidth - 1, 1, 240), Math.Clamp(Console.WindowHeight, 1, 100)), true); return;
                case ConsoleKey.J: editor.Insert("\n"); return;
                case ConsoleKey.C: Command("/stop"); return;
            }
        }
        if (completion.Handle(key, editor)) return;
        switch (key.Key)
        {
            case ConsoleKey.Enter when ctrl || key.Modifiers.HasFlag(ConsoleModifiers.Alt): editor.Insert("\n"); break;
            case ConsoleKey.Enter: Send(); break;
            case ConsoleKey.Escape: Command("/stop"); break;
            case ConsoleKey.PageUp: View(chatId).Scroll += 8; break;
            case ConsoleKey.PageDown: View(chatId).Scroll = Math.Max(0, View(chatId).Scroll - 8); break;
            case ConsoleKey.End when editor.Text.Length == 0: View(chatId).Scroll = 0; break;
            default: editor.Key(key); break;
        }
    }
    void RequestQuit()
    {
        if (!runs.Values.Any(t => !t.IsCompleted)) { quit = true; return; }
        Work(async () => { if (await Prompt(L("Quitter ?", "Quit?"), L("Les exécutions de ce CLI seront arrêtées.", "This CLI's active runs will be stopped."), [new("stay", "Continuer / Stay"), new("quit", "Arrêter et quitter / Stop and quit")]) == "quit") Post(() => quit = true); });
    }
}

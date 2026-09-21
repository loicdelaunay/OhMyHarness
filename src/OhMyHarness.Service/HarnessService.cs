using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Markdig;

namespace OhMyHarness.Service;

public sealed partial class HarnessService(string database, Func<string, JsonObject, CancellationToken, Task<JsonNode?>> host, Func<object, Task> emit) : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static readonly MarkdownPipeline Markdown = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    readonly ConcurrentDictionary<int, ConversationSession> runs = new();
    readonly SemaphoreSlim permissions = new(1, 1), tools = new(1, 1), startup = new(1, 1);
    readonly List<Process> servers = [];
    bool browserAccess, domAccess;
    static string S(JsonObject p, string name, string fallback = "") => p[name]?.GetValue<string>() ?? fallback;
    static int I(JsonObject p, string name, int fallback = 0) => p[name]?.GetValue<int>() ?? fallback;
    static bool B(JsonObject p, string name, bool fallback = false) => p[name]?.GetValue<bool>() ?? fallback;
    static JsonObject Obj(object value) => (JsonSerializer.SerializeToNode(value, Json) as JsonObject)!;
    HarnessDb Db() => new(database);
    public async Task Initialize() { await using var db = Db(); await db.InitializeAsync(); new CustomSkills(CustomSkills.DefaultRoot).EnsureTemplate(); }
    static object ProviderView(Provider p) => new { p.Id, p.Name, p.Kind, p.CompositeJson, p.BaseUrl, p.Model, p.ContextLimit, p.SupportsImages, p.Username, p.ExecutablePath, p.AutoStart, p.OpenCodeTools, hasKey = p.ProtectedKey.Length > 0 };
    static string Html(string text)
    {
        var document = Markdig.Markdown.Parse(text, Markdown);
        LocalFileLinks.Decorate(document);
        return CodeHighlight.Html(Markdig.Markdown.ToHtml(document, Markdown));
    }
    static object MessageView(Message m) => new { m.Id, m.ChatId, m.Role, m.Content, m.State, m.InputTokens, m.OutputTokens, m.Seconds,
        html = Html(m.Content), reasoning = string.IsNullOrEmpty(m.WireJson) ? "" : JsonNode.Parse(m.WireJson)?["reasoning_content"]?.GetValue<string>() ?? "",
        attachments = m.Attachments.Select(x => new { x.Name, x.Mime, data = Convert.ToBase64String(x.Data) }) };

    public async Task<object?> Dispatch(string method, JsonObject p, CancellationToken ct)
    {
        await using var db = Db();
        switch (method)
        {
            case "inbox.list":return await Inbox(p,ct);
            case "inbox.add":return await AddInbox(p,ct);
            case "inbox.update":return await UpdateInbox(p,ct);
            case "inbox.resume":return await SendNext(I(p,"chatId"),ct);
            case "inbox.delete":await db.PendingInputs.Where(x=>x.Id==I(p,"id") && x.ChatId==I(p,"chatId")).ExecuteDeleteAsync(ct);await NotifyInbox(I(p,"chatId"));return true;
            case "terminals.list": case "terminals.create": case "terminals.delete": case "terminals.stop": case "terminals.start":
                return await DispatchTerminal(method, p, ct);
            case "context.details": return await ReadContext(I(p, "chatId"), I(p, "providerId"), ct);
            case "context.compact": return await CompactManually(p, ct);
            case "question.answer": return AnswerQuestion(p);
            case "snapshot":
                var mcpConfigError = await SyncMcpFile(ct);
                return new { platform = OperatingSystem.IsMacOS() ? "macOS" : "Windows", shell = PlatformSupport.ShellName, database, mcpConfigError, appearanceThemes = AppearanceThemes.All,
                    projects = await db.Projects.AsNoTracking().Select(x => new { x.Id, x.Name, x.SourceFolder }).ToListAsync(ct),
                    chats = await db.Chats.AsNoTracking().Select(x => new { x.Id, x.ProjectId, x.Title, x.ExecutionMode, x.OrchestrationMode, x.SandboxEnabled }).ToListAsync(ct),
                    providers = (await db.Providers.AsNoTracking().ToListAsync(ct)).Select(ProviderView),
                    mcpServers = (await db.McpServers.AsNoTracking().ToListAsync(ct)).Select(McpView),
                    state = await db.States.SingleAsync(ct), templates = await db.Templates.ToListAsync(ct),
                    permissions = await db.PermissionGrants.ToListAsync(ct), skills = Skills.Available().Select(skill => OperatingSystem.IsMacOS() ? skill with
                    { FrenchDescription = skill.FrenchDescription.Replace("Windows", "macOS").Replace("PowerShell", "zsh"), EnglishDescription = skill.EnglishDescription.Replace("Windows", "macOS").Replace("PowerShell", "zsh") } : skill), running = runs.Keys,
                    questions = questions.Select(x => new { id = x.Key, chatId = x.Value.ChatId, questions = x.Value.Questions }), browserAccess, domAccess, skillsDirectory = CustomSkills.DefaultRoot };
            case "subagents": return await db.Subagents.AsNoTracking().Where(x=>x.ChatId==I(p,"chatId")).OrderBy(x=>x.CreatedUtc).ToListAsync(ct);
            case "history":
                return (await db.Messages.AsNoTracking().Include(x => x.Attachments).Where(x => x.ChatId == I(p, "chatId")).OrderBy(x => x.Id).ToListAsync(ct)).Select(MessageView);
            case "chat.export":
                runs.TryGetValue(I(p, "chatId"), out var exportingRun);
                return await ConversationExport.CreateAsync(db, I(p, "chatId"), I(p, "providerId"), exportingRun != null,
                    browserAccess, domAccess, exportingRun?.ExportProgress);
            case "project.save":
                var project = I(p, "id") == 0 ? new Project() : await db.Projects.SingleAsync(x => x.Id == I(p, "id"), ct);
                project.Name = S(p, "name", "Projet").Trim();
                var folders = (p["folders"] as JsonArray ?? []).Select(x => LocalPreview.ValidatePath(x!.GetValue<string>())).ToList();
                if (folders.Any(x => !Directory.Exists(x))) throw new DirectoryNotFoundException("Source folder not found.");
                project.SetSourceFolders(folders);
                if (project.Id == 0) { project.Chats.Add(new Chat()); db.Projects.Add(project); }
                await db.SaveChangesAsync(ct); return new { project.Id };
            case "project.delete":
                if (runs.Values.Any(x => x.Project.Id == I(p, "id"))) throw new InvalidOperationException("Stop this project's conversations before deleting it.");
                foreach (var terminalChat in await db.Chats.Where(x => x.ProjectId == I(p, "id")).Select(x => x.Id).ToListAsync(ct)) await terminals.RemoveChatAsync(terminalChat);
                db.Projects.Remove(await db.Projects.SingleAsync(x => x.Id == I(p, "id"), ct)); await db.SaveChangesAsync(ct); return true;
            case "chat.save":
                var chat = I(p, "id") == 0 ? new Chat { ProjectId = I(p, "projectId") } : await db.Chats.SingleAsync(x => x.Id == I(p, "id"), ct);
                chat.Title = S(p, "title", "Nouvelle conversation");
                if (chat.Id == 0) db.Chats.Add(chat);
                await db.SaveChangesAsync(ct); return new { chat.Id };
            case "chat.delete":
                if (runs.ContainsKey(I(p, "id"))) throw new InvalidOperationException("Stop this conversation before deleting it.");
                await terminals.RemoveChatAsync(I(p, "id"));
                db.Chats.Remove(await db.Chats.SingleAsync(x => x.Id == I(p, "id"), ct)); await db.SaveChangesAsync(ct); return true;
            case "sandbox.review": return await ReviewSandbox(p, ct);
            case "sandbox.apply": return await ApplySandbox(p, ct);
            case "sandbox.close": CloseSandboxReview(S(p, "token")); return true;
            case "chat.modes":
                var modeChat = await db.Chats.SingleAsync(x => x.Id == I(p, "id"), ct);
                modeChat.ExecutionMode = AgentPolicy.Mode(S(p, "executionMode", modeChat.ExecutionMode));
                modeChat.SandboxEnabled = B(p, "sandboxEnabled", modeChat.SandboxEnabled);
                modeChat.OrchestrationMode = AgentPolicy.Orchestration(S(p, "orchestrationMode", modeChat.OrchestrationMode));
                await db.SaveChangesAsync(ct); return true;
            case "provider.save":
                var provider = I(p, "id") == 0 ? new Provider() : await db.Providers.SingleAsync(x => x.Id == I(p, "id"), ct);
                provider.Name = S(p, "name", "Compatible OpenAI"); provider.Kind = S(p, "kind", "openai");
                if(provider.IsComposite)
                {
                    var composite=CompositeModel.Read(S(p,"compositeJson"));var available=await db.Providers.AsNoTracking().ToListAsync(ct);composite.Validate(available);
                    var orchestrator=CompositeModel.Resolve(composite.Orchestrator,available);
                    provider.CompositeJson=composite.Json();provider.Model=orchestrator.Model;provider.BaseUrl="";provider.ProtectedKey=[];
                    provider.ContextLimit=orchestrator.ContextLimit;provider.SupportsImages=orchestrator.SupportsImages;
                    if(provider.Id==0)db.Providers.Add(provider);await db.SaveChangesAsync(ct);return ProviderView(provider);
                }
                provider.BaseUrl = S(p, "baseUrl").TrimEnd('/'); _ = ChatEngine.Endpoint(provider.BaseUrl, "models");
                provider.Model = S(p, "model"); provider.ContextLimit = Math.Clamp(I(p, "contextLimit", 128000), 1024, 10_000_000);
                provider.SupportsImages = B(p, "supportsImages", true); provider.Username = S(p, "username", "opencode");
                provider.AutoStart = B(p, "autoStart"); provider.OpenCodeTools = B(p, "openCodeTools"); provider.ExecutablePath = S(p, "executablePath");
                if (B(p, "deleteKey")) provider.ProtectedKey = [];
                if (!string.IsNullOrEmpty(S(p, "key"))) provider.ProtectedKey = await Encrypt(S(p, "key"), ct);
                if (provider.Id == 0) db.Providers.Add(provider);
                await db.SaveChangesAsync(ct); return ProviderView(provider);
            case "provider.delete":
                if((await db.Providers.Where(x=>x.Kind=="composite").ToListAsync(ct)).Any(x=>{var c=CompositeModel.Read(x.CompositeJson);return c.Agents.Prepend(c.Orchestrator).Any(a=>a.ProviderId==I(p,"id"));}) || await db.PendingInputs.AnyAsync(x=>x.ProviderId==I(p,"id"),ct))throw new InvalidOperationException("Fournisseur utilisé par un modèle composé ou un message en attente.");
                if (runs.Values.Any(x => x.SelectedProviderId == I(p, "id") || x.Provider.Id == I(p,"id") || x.AgentProviders.Values.Any(a=>a.Id==I(p,"id")))) throw new InvalidOperationException("Provider is in use by a conversation.");
                db.Providers.Remove(await db.Providers.SingleAsync(x => x.Id == I(p, "id"), ct)); await db.SaveChangesAsync(ct); return true;
            case "provider.models":
                var selected = await db.Providers.SingleAsync(x => x.Id == I(p, "id"), ct);
                if(selected.IsComposite)return new[]{selected.Model};
                var secret = await Decrypt(selected.ProtectedKey, ct);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    if (!selected.IsOpenCode) return await new ChatEngine(http).ModelsAsync(selected, secret, timeout.Token);
                    await EnsureOpenCode(selected, secret, Path.GetDirectoryName(database)!, timeout.Token);
                    return (await new OpenCodeEngine(http).ModelsAsync(selected, secret, null, timeout.Token)).Select(x => x.Reference);
                }
            case "state.save":
                var state = await db.States.SingleAsync(ct);
                if(p["featuresJson"] != null) state.FeaturesJson = FeatureSettings.Read(S(p,"featuresJson")).Json();
                state.Language = S(p, "language", state.Language) == "en" ? "en" : "fr";
                state.PermissionMode = PermissionModes.Normalize(S(p, "permissionMode", state.PermissionMode));
                state.AutoContinue = B(p, "autoContinue", state.AutoContinue);
                state.ShowReasoningDetails = B(p, "showReasoningDetails", state.ShowReasoningDetails);
                state.EnabledSkills = string.Join(',', S(p, "enabledSkills", state.EnabledSkills).Split(',').Where(id => Skills.Available().Any(x => x.Id == id)));
                state.ThinkingLevel = S(p, "thinkingLevel", state.ThinkingLevel);
                state.ProviderId = I(p, "providerId", state.ProviderId);
                state.ProjectId = p["projectId"]?.GetValue<int>() ?? state.ProjectId; state.ChatId = p["chatId"]?.GetValue<int>() ?? state.ChatId;
                await db.SaveChangesAsync(ct); return true;
            case "template.save":
                var template = I(p, "id") == 0 ? new PromptTemplate() : await db.Templates.SingleAsync(x => x.Id == I(p, "id"), ct);
                template.Name = S(p, "name"); template.Content = S(p, "content");
                if (string.IsNullOrWhiteSpace(template.Name) || string.IsNullOrWhiteSpace(template.Content)) throw new ArgumentException("Name and content required.");
                if (template.Id == 0) db.Templates.Add(template); await db.SaveChangesAsync(ct); return template;
            case "template.delete":
                db.Templates.Remove(await db.Templates.SingleAsync(x => x.Id == I(p, "id"), ct)); await db.SaveChangesAsync(ct); return true;
            case "permission.revoke":
                db.PermissionGrants.Remove(await db.PermissionGrants.SingleAsync(x => x.Id == I(p, "id"), ct)); await db.SaveChangesAsync(ct); return true;
            case "browser.access": browserAccess = B(p, "enabled"); domAccess = B(p, "dom"); return true;
            case "files.list": case "files.read": case "git": case "git.files": case "git.diff": case "git.preview": case "terminal":
                var workspace = await db.Projects.SingleAsync(x => x.Id == I(p, "projectId"), ct);
                var source = new SourceAccess(workspace.GetSourceFolders());
                if (method == "files.list") return source.List(S(p, "path", "."));
                if (method == "files.read") return await source.ReadAsync(S(p, "path"), ct);
                if (method == "git") return await Git(workspace, ct);
                if (method == "git.files") return new { hasRepository = workspace.GetSourceFolders().Any(WorkspaceTools.HasGitRepository), files = await GitWorkspace.ListWithStatsAsync(workspace.GetSourceFolders(), ct) };
                if (method is "git.diff" or "git.preview")
                {
                    var file = (await GitWorkspace.ListAsync(workspace.GetSourceFolders(), ct)).FirstOrDefault(x => x.Repository == S(p, "repository") && x.Path == S(p, "path"));
                    var diff = file == null ? "Aucune modification / No changes." : await GitWorkspace.DiffAsync(file, ct); return method == "git.preview" ? GitWorkspace.ParseDiff(diff) : diff;
                }
                return await WorkspaceTools.ShellAsync(S(p, "command"), Root(workspace), ct);
            case "preview":
                var previewChat = await db.Chats.SingleAsync(x => x.Id == I(p, "chatId"), ct);
                var previewProject = await db.Projects.SingleAsync(x => x.Id == previewChat.ProjectId, ct);
                return await Preview(previewProject, S(p, "path"), ct, previewChat.Id);
            case "mcp.json.get": case "mcp.json.save": case "mcp.save": case "mcp.delete": case "mcp.toggle": case "mcp.test": return await DispatchMcp(method, p, ct);
            case "send": return await Send(p, ct);
            case "stop": if (runs.TryGetValue(I(p, "chatId"), out var running)) running.Cancellation.Cancel(); await terminals.StopChatAsync(I(p, "chatId")); return true;
            default: throw new ArgumentException("Unknown method: " + method);
        }
    }

    async Task<byte[]> Encrypt(string key, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) return KeyVault.Encrypt(key);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Windows and macOS are supported.");
        var encrypted = await host("key.encrypt", Obj(new { text = key }), ct);
        return Encoding.UTF8.GetBytes("OMH-MAC-1:" + encrypted!.GetValue<string>());
    }
    async Task<string> Decrypt(byte[] key, CancellationToken ct)
    {
        if (key.Length == 0) return "";
        if (Encoding.UTF8.GetString(key).StartsWith("OMH-MAC-1:", StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("Re-enter this API key on Windows; it was encrypted with the macOS Keychain.");
            return (await host("key.decrypt", Obj(new { data = Encoding.UTF8.GetString(key)[10..] }), ct))!.GetValue<string>();
        }
        if (OperatingSystem.IsWindows()) return KeyVault.Decrypt(key);
        throw new InvalidOperationException("Ressaisissez cette clé API sur Mac : elle est protégée par Windows DPAPI.");
    }
    public void CancelAll() { foreach (var run in runs.Values) run.Cancellation.Cancel(); }
    public ValueTask DisposeAsync()
    {
        CancelAll(); terminals.Dispose(); http.Dispose();
        foreach (var process in servers) { try { if (!process.HasExited) process.Kill(true); } catch { } process.Dispose(); }
        return ValueTask.CompletedTask;
    }
}

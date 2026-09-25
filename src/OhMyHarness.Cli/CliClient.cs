using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using OhMyHarness.Core.Hosting;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Cli;

public sealed record WorkspaceSnapshot(AppState State, List<Project> Projects, List<Chat> Chats, List<Provider> Providers, List<McpServer> Servers);

public sealed class CliClient : IAsyncDisposable
{
    public static readonly HashSet<string> DesktopSkills = [BrowserSkillAccess.Access, BrowserSkillAccess.Dom, "mouse_control", "keyboard_control", "screenshots", "applications"];
    public string Database { get; }
    public HarnessService Service { get; }
    public CliClient(CliOptions options, Func<string, JsonObject, CancellationToken, Task<JsonNode?>> host, Func<object, Task> emit)
    {
        Database = options.Database ?? Path.Combine(PortableStorage.Root, "database.sqlite");
        PortableStorage.UseDatabase(Database);
        PortableStorage.EnsureWritable();
        Service = new(Database, host, emit, new HarnessServiceOptions
        {
            DisabledSkills = DesktopSkills,
            SupportsLocalPreview = false,
            UseNativeKeyVault = true,
            PermissionModeOverride = options.Run ? options.Allow ? PermissionModes.Allow : PermissionModes.Deny : null
        });
    }
    public HarnessDb OpenDb() => new(Database);
    public static JsonObject J(object value) => (JsonSerializer.SerializeToNode(value, HarnessService.Json) as JsonObject)!;
    public async Task<JsonNode?> Call(string method, object? value = null, CancellationToken ct = default) =>
        JsonSerializer.SerializeToNode(await Service.Dispatch(method, value == null ? [] : J(value), ct), HarnessService.Json);

    public async Task<(WorkspaceSnapshot Snapshot, int ChatId, int ProviderId)> Initialize(CliOptions options, CancellationToken ct)
    {
        await Service.Initialize();
        await Call("mcp.json.get", ct: ct);
        await using var db = OpenDb();
        var state = await db.States.SingleAsync(ct);
        Chat? selected = options.Chat is int chatId ? await db.Chats.SingleOrDefaultAsync(c => c.Id == chatId, ct) : null;
        if (options.Chat != null && selected == null) throw new ArgumentException("Conversation not found: " + options.Chat);
        if (options.Directory is { } folder)
        {
            var all = await db.Projects.ToListAsync(ct);
            var project = all.FirstOrDefault(p => p.GetSourceFolders().Any(f => PlatformSupport.PathComparer.Equals(Path.GetFullPath(f), folder)));
            if (selected != null && project?.Id != selected.ProjectId)
                throw new ArgumentException("--chat belongs to a different project. Omit --project to resume it.");
            if (project == null)
            {
                project = new Project { Name = new DirectoryInfo(folder).Name, Chats = [new Chat()] };
                project.SetSourceFolders([folder]); db.Projects.Add(project); await db.SaveChangesAsync(ct);
            }
            selected ??= await db.Chats.Where(c => c.ProjectId == project.Id).OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);
            if (selected == null) { selected = new Chat { ProjectId = project.Id }; db.Chats.Add(selected); await db.SaveChangesAsync(ct); }
        }
        selected ??= await db.Chats.SingleOrDefaultAsync(c => c.Id == state.ChatId, ct) ?? await db.Chats.OrderByDescending(c => c.Id).FirstOrDefaultAsync(ct);
        if (selected == null)
        {
            var project = await db.Projects.FirstOrDefaultAsync(ct);
            if (project == null) { project = new Project { Name = "Espace personnel" }; db.Projects.Add(project); await db.SaveChangesAsync(ct); }
            selected = new Chat { ProjectId = project.Id }; db.Chats.Add(selected); await db.SaveChangesAsync(ct);
        }
        // Automated runs get their own history unless resumption is explicitly requested.
        if (options.Run && options.Chat == null)
        {
            selected = new Chat { ProjectId = selected.ProjectId }; db.Chats.Add(selected);
        }
        if (options.Run) selected.ExecutionMode = options.Execute ? "execute" : "plan";
        var provider = options.Provider ?? state.ProviderId;
        if (!await db.Providers.AnyAsync(p => p.Id == provider, ct))
        {
            if (options.Provider != null) throw new ArgumentException("Provider not found: " + provider);
            provider = await db.Providers.Select(p => p.Id).FirstOrDefaultAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        return (await Snapshot(ct), selected.Id, provider);
    }

    public async Task<WorkspaceSnapshot> Snapshot(CancellationToken ct = default)
    {
        await using var db = OpenDb();
        return new(await db.States.AsNoTracking().SingleAsync(ct), await db.Projects.AsNoTracking().ToListAsync(ct),
            await db.Chats.AsNoTracking().OrderByDescending(c => c.IsFavorite).ThenByDescending(c => c.Id).ToListAsync(ct), await db.Providers.AsNoTracking().ToListAsync(ct),
            await db.McpServers.AsNoTracking().ToListAsync(ct));
    }
    public async Task<List<Message>> History(int chatId, CancellationToken ct)
    {
        await using var db = OpenDb();
        // Images remain in SQLite; loading the timeline does not materialize their binary data.
        var items = await db.Messages.AsNoTracking().Where(m => m.ChatId == chatId).OrderBy(m => m.Id).ToListAsync(ct);
        var names = await db.Set<Attachment>().AsNoTracking().Where(a => db.Messages.Any(m => m.Id == a.MessageId && m.ChatId == chatId))
            .Select(a => new { a.MessageId, a.Name }).ToListAsync(ct);
        foreach (var item in items) item.Attachments = names.Where(a => a.MessageId == item.Id).Select(a => new Attachment { Name = a.Name }).ToList();
        return items;
    }
    public async Task State(Action<AppState> update, CancellationToken ct = default)
    {
        await using var db = OpenDb(); var state = await db.States.SingleAsync(ct); update(state); await db.SaveChangesAsync(ct);
        AppLog.Configure(FeatureSettings.Read(state.FeaturesJson));
    }
    public async Task<int> CreateChat(int projectId, CancellationToken ct)
    {
        var result = await Call("chat.save", new { projectId, title = "Nouvelle conversation" }, ct);
        return result!["id"]!.GetValue<int>();
    }
    public async Task SelectModel(int providerId, string model, CancellationToken ct)
    {
        await using var db = OpenDb(); var provider = await db.Providers.SingleAsync(p => p.Id == providerId, ct);
        provider.Model = model; (await db.States.SingleAsync(ct)).ProviderId = providerId; await db.SaveChangesAsync(ct);
    }
    public async Task<int> SaveProvider(Provider provider, string key, CancellationToken ct)
    {
        _ = ChatEngine.Endpoint(provider.BaseUrl, "models");
        await using var db = OpenDb();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        provider.ProtectedKey = key.Length == 0 ? [] : KeyVault.Encrypt(key);
        db.Providers.Add(provider); await db.SaveChangesAsync(ct);
        (await db.States.SingleAsync(ct)).ProviderId = provider.Id; await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return provider.Id;
    }
    public async Task<List<string>> RefreshModels(int providerId, CancellationToken ct)
    {
        var models = (await Call("provider.models", new { id = providerId }, ct))!.Deserialize<List<string>>()!;
        await using var db = OpenDb(); var provider = await db.Providers.SingleAsync(p => p.Id == providerId, ct);
        ProviderModels.Refresh(provider, models); ProviderModels.Select(provider, models); await db.SaveChangesAsync(ct); return models;
    }
    public async ValueTask DisposeAsync() => await Service.DisposeAsync();
}

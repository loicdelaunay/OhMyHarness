using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core;

/// <summary>A generation's captured inputs and private persistence context, independent of UI selection.</summary>
public class ConversationSession : IDisposable
{
    public WorkflowTools? Workflow { get; set; }
    public ToolLoopGuard LoopGuard { get; } = new();
    public SandboxWorkspace? Sandbox { get; private set; }
    public string? SandboxEngine { get; private set; }
    public async Task PrepareSandboxAsync(CancellationToken ct)
    {
        if (!Chat.SandboxEnabled) return;
        if (Provider.IsOpenCode) throw new InvalidOperationException("Sandbox : OpenCode n'est pas encore isolé. Choisissez un fournisseur OpenAI compatible ou DeepSeek / OpenCode is not supported in sandbox mode.");
        SandboxEngine = await SandboxContainer.CheckAsync(ct);
        Sandbox = await SandboxWorkspace.OpenAsync(Db.Database.GetDbConnection().DataSource, Chat.Id, Project.GetSourceFolders(), ct);
        Project.SetSourceFolders(Sandbox.WorkRoots);
        Cancellation.CancelAfter(TimeSpan.FromMinutes(30));
    }
    public Chat Chat { get; }
    public Project Project { get; }
    public Provider Provider { get; }
    public AppState Options { get; }
    public string Prompt { get; }
    public List<Attachment> Images { get; }
    public HarnessDb Db { get; }
    public CancellationTokenSource Cancellation { get; }

    public ConversationSession(Chat chat, Project project, Provider provider, AppState options,
        string prompt, IEnumerable<Attachment> images, string? databasePath = null)
    {
        Chat = new Chat { Id = chat.Id, ProjectId = chat.ProjectId, Title = chat.Title,
            SandboxEnabled = chat.SandboxEnabled, ExecutionMode = AgentPolicy.Mode(chat.ExecutionMode), OrchestrationMode = AgentPolicy.Orchestration(chat.OrchestrationMode) };
        Project = new Project { Id = project.Id, Name = project.Name, SourceFolder = project.SourceFolder };
        Provider = JsonSerializer.Deserialize<Provider>(JsonSerializer.Serialize(provider))!;
        Options = new AppState { Language = options.Language, EnabledSkills = options.EnabledSkills, ThinkingLevel = options.ThinkingLevel, AutoContinue = options.AutoContinue };
        Prompt = prompt;
        Images = images.Select(x => new Attachment { Name = x.Name, Mime = x.Mime, Data = [.. x.Data] }).ToList();
        Db = new HarnessDb(databasePath);
        Db.Attach(Chat);
        Cancellation = new CancellationTokenSource();
    }

    public void Dispose()
    {
        Sandbox?.Dispose();
        Cancellation.Dispose();
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}

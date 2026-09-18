using System.Text.Json;

namespace OhMyHarness.Core;

/// <summary>A generation's captured inputs and private persistence context, independent of UI selection.</summary>
public class ConversationSession : IDisposable
{
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
        Chat = new Chat { Id = chat.Id, ProjectId = chat.ProjectId, Title = chat.Title };
        Project = new Project { Id = project.Id, Name = project.Name, SourceFolder = project.SourceFolder };
        Provider = JsonSerializer.Deserialize<Provider>(JsonSerializer.Serialize(provider))!;
        Options = new AppState { Language = options.Language, EnabledSkills = options.EnabledSkills, ThinkingLevel = options.ThinkingLevel };
        Prompt = prompt;
        Images = images.Select(x => new Attachment { Name = x.Name, Mime = x.Mime, Data = [.. x.Data] }).ToList();
        Db = new HarnessDb(databasePath);
        Db.Attach(Chat);
        Cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(provider.IsOpenCode ? 30 : 10));
    }

    public void Dispose()
    {
        Cancellation.Dispose();
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}

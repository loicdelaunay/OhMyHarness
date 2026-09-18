using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Security.Cryptography;
using System.Text;

namespace OhMyHarness.Core;

public sealed class Project
{
    public int Id { get; set; }
    public string Name { get; set; } = "Mon projet";
    public string SourceFolder { get; set; } = "";
    public List<Chat> Chats { get; set; } = [];
    public override string ToString() => Name;
}
public sealed class Chat
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = "Nouvelle conversation";
    public List<Message> Messages { get; set; } = [];
    public override string ToString() => Title;
}
public sealed class Message
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string WireJson { get; set; } = "";
    public string State { get; set; } = "complete";
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double Seconds { get; set; }
    public List<Attachment> Attachments { get; set; } = [];
}
public sealed class Attachment
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string Name { get; set; } = "";
    public string Mime { get; set; } = "image/png";
    public byte[] Data { get; set; } = [];
}
public sealed class Provider
{
    public int Id { get; set; }
    public string Name { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4.1-mini";
    public byte[] ProtectedKey { get; set; } = [];
    public int ContextLimit { get; set; } = 128000;
    public bool SupportsImages { get; set; } = true;
    public override string ToString() => Name;
}
public sealed class AppState
{
    public int Id { get; set; } = 1;
    public int? ProjectId { get; set; }
    public int? ChatId { get; set; }
    public int ProviderId { get; set; } = 1;
    public string BrowserUrl { get; set; } = "https://www.bing.com";
    public string Language { get; set; } = "fr";
    public string EnabledSkills { get; set; } = "sources,web";
    public string ThinkingLevel { get; set; } = "auto";
}
public sealed class PromptTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public override string ToString() => Name;
}
public sealed class HarnessDb : DbContext
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OhMyHarness");
    readonly string path;
    public HarnessDb(string? path = null) => this.path = path ?? Path.Combine(DataDirectory, "harness.db");
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<AppState> States => Set<AppState>();
    public DbSet<PromptTemplate> Templates => Set<PromptTemplate>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={path}")
               .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Project>().HasMany(x => x.Chats).WithOne().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Chat>().HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Message>().HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await Database.MigrateAsync();
        if (!await Providers.AnyAsync())
        {
            Providers.AddRange(new Provider(), new Provider { Name = "DeepSeek", BaseUrl = "https://api.deepseek.com", Model = "deepseek-flash", SupportsImages = true });
            States.Add(new AppState());
            Projects.Add(new Project { Name = "Espace personnel", Chats = [new Chat()] });
            await SaveChangesAsync();
        }
    }
}
public sealed class DesignFactory : IDesignTimeDbContextFactory<HarnessDb>
{
    public HarnessDb CreateDbContext(string[] args) => new();
}
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class KeyVault
{
    public static byte[] Encrypt(string key) => ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
    public static string Decrypt(byte[] key) => key.Length == 0 ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(key, null, DataProtectionScope.CurrentUser));
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Data.Sqlite;
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

    public List<string> GetSourceFolders() =>
        string.IsNullOrWhiteSpace(SourceFolder)
            ? []
            : SourceFolder.Split(['|', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    public void SetSourceFolders(IEnumerable<string> folders)
    {
        SourceFolder = string.Join('|', folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
    }
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
    public string Kind { get; set; } = "openai";
    public string Username { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public bool AutoStart { get; set; }
    public bool OpenCodeTools { get; set; }
    public bool IsOpenCode => Kind.Equals("opencode", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => Id > 0 ? $"{Name} · #{Id}" : Name;
}
public sealed class ExternalChatSession
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int ProviderId { get; set; }
    public string SessionId { get; set; } = "";
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
    public string PermissionMode { get; set; } = PermissionModes.Ask;
}
public static class PermissionModes
{
    public const string Deny = "deny";
    public const string Ask = "ask";
    public const string Allow = "allow";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Deny => Deny,
        Allow => Allow,
        _ => Ask
    };

    // null means that the regular per-scope grant/dialog flow must continue.
    public static bool? AutomaticDecision(string? value) => Normalize(value) switch
    {
        Deny => false,
        Allow => true,
        _ => null
    };
}
public sealed class PromptTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public override string ToString() => Name;
}
public sealed class PermissionGrant
{
    public int Id { get; set; }
    public string Scope { get; set; } = "";
    public string Name { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class HarnessDb : DbContext
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OhMyHarness");
    public static string DatabasePath => Path.Combine(AppContext.BaseDirectory, "database.sqlite");
    public static string LegacyDatabasePath => Path.Combine(DataDirectory, "harness.db");
    readonly string path;
    readonly bool usesDefaultPath;
    public HarnessDb(string? path = null)
    {
        usesDefaultPath = string.IsNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(usesDefaultPath ? DatabasePath : path!);
    }
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<AppState> States => Set<AppState>();
    public DbSet<PromptTemplate> Templates => Set<PromptTemplate>();
    public DbSet<PermissionGrant> PermissionGrants => Set<PermissionGrant>();
    public DbSet<ExternalChatSession> ExternalChatSessions => Set<ExternalChatSession>();
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path }.ToString())
               .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Project>().HasMany(x => x.Chats).WithOne().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Chat>().HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<Message>().HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<PermissionGrant>().HasIndex(x => x.Scope).IsUnique();
        model.Entity<ExternalChatSession>().HasIndex(x => new { x.ChatId, x.ProviderId }).IsUnique();
        model.Entity<ExternalChatSession>().HasOne<Chat>().WithMany().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<ExternalChatSession>().HasOne<Provider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);
    }
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (usesDefaultPath) await ImportLegacyDatabaseAsync();
        await Database.MigrateAsync();
        if (!await States.AnyAsync())
        {
            if (!await Providers.AnyAsync()) Providers.AddRange(new Provider(), new Provider { Name = "DeepSeek", BaseUrl = "https://api.deepseek.com", Model = "deepseek-flash", SupportsImages = true });
            States.Add(new AppState());
            if (!await Projects.AnyAsync()) Projects.Add(new Project { Name = "Espace personnel", Chats = [new Chat()] });
            await SaveChangesAsync();
        }
        var legacyProviders = await Providers.Where(x => x.Kind == "").ToListAsync();
        if (legacyProviders.Count > 0)
        {
            foreach (var item in legacyProviders) item.Kind = "openai";
            await SaveChangesAsync();
        }
    }

    async Task ImportLegacyDatabaseAsync()
    {
        if (File.Exists(path) || !File.Exists(LegacyDatabasePath)) return;
        await CopyDatabaseAsync(LegacyDatabasePath, path);
    }

    public static async Task CopyDatabaseAsync(string sourcePath, string destinationPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Base SQLite source introuvable.", sourcePath);
        if (File.Exists(destinationPath)) throw new IOException("La base SQLite de destination existe déjà.");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporary = destinationPath + ".migrating-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString());
            await source.OpenAsync();
            await destination.OpenAsync();
            source.BackupDatabase(destination);
            await destination.CloseAsync();
            await source.CloseAsync();
            try { File.Move(temporary, destinationPath); }
            catch (IOException) when (File.Exists(destinationPath)) { }
        }
        catch (Exception ex)
        {
            throw new IOException($"Impossible de copier la base SQLite vers {destinationPath}.", ex);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var candidate = temporary + suffix;
                if (File.Exists(candidate)) try { File.Delete(candidate); } catch { }
            }
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

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace OhMyHarness.Core;

public sealed class MemoryEntry
{
    public int Id { get; set; }
    public string Scope { get; set; } = "conversation";
    public string Category { get; set; } = "project";
    public int? ProjectId { get; set; }
    public int? ChatId { get; set; }
    public string Partition { get; set; } = "";
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Tags { get; set; } = "";
    public int? OriginChatId { get; set; }
    public string Author { get; set; } = "user";
    public int Version { get; set; } = 1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed record MemoryAccess(int ProjectId, int? ChatId, bool Conversation = true, bool Shared = true);
public sealed record MemoryHit(int Id, string Scope, string Category, string Key, string Title, string Tags,
    string Excerpt, int Version, DateTime UpdatedUtc);
public sealed record MemoryPage(IReadOnlyList<MemoryHit> Items, bool HasMore, int NextOffset);
public sealed record MemoryDraft(string Scope, string Category, string Key, string Title, string Content, string Tags = "");

/// <summary>All queries, including ID lookups and mutations, enforce the calling conversation's scope.</summary>
public sealed class MemoryStore(string databasePath)
{
    static IQueryable<MemoryEntry> Visible(HarnessDb db, MemoryAccess access) => db.Memories.Where(x =>
        x.Scope == "conversation" && access.Conversation && access.ChatId != null && x.ChatId == access.ChatId && x.ProjectId == access.ProjectId ||
        x.Scope == "shared" && access.Shared && (x.Category != "project" && x.ProjectId == null || x.Category == "project" && x.ProjectId == access.ProjectId));

    // A literal-word grammar avoids exposing FTS operators and supports accents and prefix search.
    public static string SearchExpression(string query)
    {
        if (query.Length > 1000) throw new ArgumentException("Recherche : 1000 caractères maximum.");
        var words = Regex.Matches(query, @"[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)
            .Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        if (words.Length == 0 && !string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Indiquez au moins un mot à rechercher.");
        return string.Join(" AND ", words.Select(x => "\"" + x + "\"*"));
    }

    public Task<MemoryPage> SearchAsync(MemoryAccess access, string query = "", string scope = "all", string category = "all", int offset = 0, int limit = 20, CancellationToken ct = default) => Task.Run(async () =>
    {
        ValidateFilter(scope, category);
        if (offset < 0 || offset > 100000 || limit is < 1 or > 50) throw new ArgumentException("Pagination invalide (1 à 50 résultats).");
        var expression = SearchExpression(query);
        await using var db = new HarnessDb(databasePath);
        var entries = Visible(db, access).AsNoTracking();
        if (scope != "all") entries = entries.Where(x => x.Scope == scope);
        if (category != "all") entries = entries.Where(x => x.Category == category);
        IQueryable<MemoryHit> hits;
        if (expression.Length == 0) hits = entries.OrderByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.Id)
            .Select(x => new MemoryHit(x.Id, x.Scope, x.Category, x.Key, x.Title, x.Tags,
                x.Content.Length > 400 ? x.Content.Substring(0, 400) + "…" : x.Content, x.Version, x.UpdatedUtc));
        else
        {
            // Match and access restrictions both execute in SQLite, before LIMIT.
            var ranked = db.Database.SqlQuery<MemoryRank>($"SELECT rowid AS Id, bm25(MemorySearch, 4.0, 6.0, 1.0, 3.0) AS Score, snippet(MemorySearch, 2, '[', ']', '…', 40) AS Excerpt FROM MemorySearch WHERE MemorySearch MATCH {expression}");
            hits = from entry in entries join rank in ranked on entry.Id equals rank.Id orderby rank.Score, entry.Id
                select new MemoryHit(entry.Id, entry.Scope, entry.Category, entry.Key, entry.Title, entry.Tags,
                    rank.Excerpt.Length > 600 ? rank.Excerpt.Substring(0, 600) + "…" : rank.Excerpt, entry.Version, entry.UpdatedUtc);
        }
        var found = await hits.Skip(offset).Take(limit + 1).ToListAsync(ct);
        return new MemoryPage(found.Take(limit).ToList(), found.Count > limit, offset + Math.Min(limit, found.Count));
    }, ct);

    sealed class MemoryRank { public int Id { get; set; } public double Score { get; set; } public string Excerpt { get; set; } = ""; }

    public Task<MemoryEntry> ReadAsync(MemoryAccess access, int id, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var db = new HarnessDb(databasePath);
        return await Visible(db, access).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Mémoire introuvable dans cette portée.");
    }, ct);

    public Task<MemoryEntry> SaveAsync(MemoryAccess access, MemoryDraft draft, int? id = null, int? expectedVersion = null, string author = "user", CancellationToken ct = default) => Task.Run(async () =>
    {
        ValidateFilter(draft.Scope, draft.Category, allowAll: false);
        if (draft.Scope == "conversation" && (!access.Conversation || access.ChatId == null) || draft.Scope == "shared" && !access.Shared)
            throw new UnauthorizedAccessException("Ce niveau de mémoire est désactivé.");
        var key = draft.Key.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(key, @"^[\p{L}\p{N}][\p{L}\p{N}_.\-]{0,119}$")) throw new ArgumentException("Clé : 1 à 120 lettres, chiffres, points, tirets ou underscores.");
        var title = draft.Title.Trim(); var content = draft.Content.Trim(); var tags = draft.Tags.Trim();
        if (title.Length is < 1 or > 200 || content.Length is < 1 or > 16000 || tags.Length > 500) throw new ArgumentException("Titre : 1–200 caractères ; contenu : 1–16000 ; tags : 500 maximum.");
        await using var db = new HarnessDb(databasePath);
        if (!await db.Projects.AnyAsync(x => x.Id == access.ProjectId, ct) || access.ChatId != null && !await db.Chats.AnyAsync(x => x.Id == access.ChatId && x.ProjectId == access.ProjectId, ct))
            throw new InvalidOperationException("Projet ou conversation indisponible.");
        var entry = id == null ? new MemoryEntry() : await Visible(db, access).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Mémoire introuvable dans cette portée.");
        if (id != null && (expectedVersion == null || entry.Version != expectedVersion)) throw new InvalidOperationException("Mémoire modifiée entre-temps. Relisez-la avant de la modifier.");
        if (id != null && (entry.Scope != draft.Scope || entry.Category != draft.Category || entry.Key != key)) throw new ArgumentException("La portée, la catégorie et la clé d’une mémoire existante sont immuables. Créez une autre entrée pour la déplacer.");
        entry.Scope = draft.Scope; entry.Category = draft.Category; entry.Key = key;
        entry.ChatId = draft.Scope == "conversation" ? access.ChatId : null;
        entry.ProjectId = draft.Scope == "conversation" || draft.Category == "project" ? access.ProjectId : null;
        entry.Partition = entry.ChatId != null ? $"conversation:{entry.ChatId}" : entry.ProjectId != null ? $"project:{entry.ProjectId}" : "shared";
        entry.Title = title; entry.Content = content; entry.Tags = tags; entry.UpdatedUtc = DateTime.UtcNow;
        entry.Author = author; if (id == null) entry.OriginChatId = access.ChatId;
        if (id == null) db.Memories.Add(entry); else entry.Version++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("Mémoire modifiée entre-temps. Relisez-la avant de la modifier."); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        { throw new InvalidOperationException("Cette clé existe déjà dans cette catégorie et cette portée. Recherchez puis modifiez l’entrée existante."); }
        return entry;
    }, ct);

    public Task DeleteAsync(MemoryAccess access, int id, int expectedVersion, CancellationToken ct = default) => Task.Run(async () =>
    {
        await using var db = new HarnessDb(databasePath);
        if (await Visible(db, access).Where(x => x.Id == id && x.Version == expectedVersion).ExecuteDeleteAsync(ct) != 1)
            throw new InvalidOperationException("Mémoire absente, inaccessible ou modifiée. Actualisez la liste.");
    }, ct);

    static void ValidateFilter(string scope, string category, bool allowAll = true)
    {
        if (!(scope is "conversation" or "shared" || allowAll && scope == "all") || !(category is "project" or "general" or "user" || allowAll && category == "all"))
            throw new ArgumentException("Portée ou catégorie de mémoire inconnue.");
    }
}

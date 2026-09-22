using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class ConversationBranches
{
    public static async Task<Chat> CreateAsync(string database, int chatId, int messageId, bool resume, CancellationToken ct = default)
    {
        await using var db = new HarnessDb(database);
        var original = await db.Chats.SingleAsync(x => x.Id == chatId, ct);
        var messages = await db.Messages.Where(x => x.ChatId == chatId).Include(x => x.Attachments).OrderBy(x => x.Id).ToListAsync(ct);
        var boundary = messages.SingleOrDefault(x => x.Id == messageId && x.Role is "user" or "assistant") ?? throw new ArgumentException("Message de départ introuvable.");
        if (boundary.State is not ("complete" or "compacted")) throw new InvalidOperationException("Choisissez un message terminé.");
        if (boundary.WireJson.Length > 0 && JsonNode.Parse(boundary.WireJson)?["tool_calls"] is JsonArray calls && calls.Count > 0) throw new InvalidOperationException("Choisissez la réponse finale ou le message utilisateur précédent, pour conserver les échanges d’outils complets.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = new Chat { ProjectId = original.ProjectId, Title = (resume ? "Sauvegarde · " : "Fork · ") + original.Title,
            ExecutionMode = original.ExecutionMode, OrchestrationMode = original.OrchestrationMode, SandboxEnabled = original.SandboxEnabled, ResourcePathsJson = original.ResourcePathsJson };
        db.Chats.Add(branch); await db.SaveChangesAsync(ct);
        foreach (var message in messages.Where(x => resume || x.Id <= messageId && x.Role is not ("tasks" or "compaction")))
            db.Messages.Add(new Message { ChatId = branch.Id, Role = message.Role, Content = message.Content, CompatibilityNotice = message.CompatibilityNotice, WireJson = message.WireJson, State = !resume && message.State == "compacted" ? "complete" : message.State,
                InputTokens = message.InputTokens, OutputTokens = message.OutputTokens, Seconds = message.Seconds,
                Attachments = message.Attachments.Select(a => new Attachment { Name = a.Name, Mime = a.Mime, Data = a.Data.ToArray() }).ToList() });
        if (resume)
        {
            db.Messages.RemoveRange(messages.Where(x => x.Id > messageId || x.Role is "tasks" or "compaction"));
            foreach (var retained in messages.Where(x => x.Id <= messageId && x.State == "compacted" && x.Role != "compaction")) retained.State = "complete";
            await db.PendingInputs.Where(x => x.ChatId == chatId).ExecuteDeleteAsync(ct);
            await db.ExternalChatSessions.Where(x => x.ChatId == chatId).ExecuteDeleteAsync(ct);
            await db.Subagents.Where(x => x.ChatId == chatId).ExecuteUpdateAsync(set => set.SetProperty(x => x.ChatId, branch.Id), ct);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return resume ? original : branch;
    }
}

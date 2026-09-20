using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace OhMyHarness.Service;

public sealed partial class HarnessService
{
    sealed record SandboxReview(int ChatId, SandboxWorkspace.Review Review, DateTime Expires);
    readonly ConcurrentDictionary<string, SandboxReview> sandboxReviews = new();
    async Task<SandboxWorkspace> OpenSandbox(int chatId, CancellationToken ct)
    {
        if (runs.ContainsKey(chatId)) throw new InvalidOperationException("Attendez la fin de la conversation / Wait for the conversation to finish.");
        await using var db = Db();
        var chat = await db.Chats.SingleAsync(x => x.Id == chatId, ct);
        var project = await db.Projects.SingleAsync(x => x.Id == chat.ProjectId, ct);
        return await SandboxWorkspace.OpenAsync(database, chatId, project.GetSourceFolders(), ct);
    }
    async Task<object> ReviewSandbox(JsonObject p, CancellationToken ct)
    {
        foreach (var item in sandboxReviews.Where(x => x.Value.Expires < DateTime.UtcNow)) sandboxReviews.TryRemove(item.Key, out _);
        if (sandboxReviews.Count >= 20) throw new InvalidOperationException("Close previous sandbox reviews first.");
        using var workspace = await OpenSandbox(I(p, "chatId"), ct);
        var review = await workspace.ReviewAsync(ct);
        var token = Guid.NewGuid().ToString("N");
        sandboxReviews[token] = new(I(p, "chatId"), review, DateTime.UtcNow.AddMinutes(10));
        return new { token, review.Diff, review.Count };
    }
    async Task<object> ApplySandbox(JsonObject p, CancellationToken ct)
    {
        if (!sandboxReviews.TryRemove(S(p, "token"), out var pending) || pending.Expires < DateTime.UtcNow || pending.ChatId != I(p, "chatId"))
            throw new InvalidOperationException("Aperçu expiré : ouvrez une nouvelle revue / Review expired.");
        using var workspace = await OpenSandbox(pending.ChatId, ct);
        var current = await workspace.ReviewAsync(ct);
        if (current.Diff != pending.Review.Diff || current.Count != pending.Review.Count)
            throw new InvalidOperationException("La sandbox a changé : ouvrez une nouvelle revue / Sandbox changed since review.");
        // Apply the actual reviewed bytes, never a fresh replacement hidden behind an old diff.
        return await workspace.ApplyAsync(pending.Review, ct);
    }
    void CloseSandboxReview(string token) => sandboxReviews.TryRemove(token, out _);
}

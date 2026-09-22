using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core.Hosting;

public sealed partial class HarnessService
{
    async Task<ContextDetails> ReadContext(int chatId, int providerId, CancellationToken ct)
    {
        await using var db = Db();
        var limit = await db.Providers.Where(x => x.Id == providerId).Select(x => x.ContextLimit).SingleAsync(ct);
        return ContextDetails.From(await db.Messages.AsNoTracking().Include(x => x.Attachments).Where(x => x.ChatId == chatId).OrderBy(x => x.Id).ToListAsync(ct), limit);
    }
    async Task<object> CompactManually(JsonObject p, CancellationToken lifetime)
    {
        await using var db = Db();
        var chat = await db.Chats.SingleAsync(x => x.Id == I(p, "chatId"), lifetime);
        var project = await db.Projects.SingleAsync(x => x.Id == chat.ProjectId, lifetime);
        var provider = await db.Providers.SingleAsync(x => x.Id == I(p, "providerId"), lifetime);
        using var run = new ConversationSession(chat, project, provider, await db.States.SingleAsync(lifetime), "", [], database,await db.Providers.ToListAsync(lifetime));
        provider=run.Provider;
        if (!runs.TryAdd(chat.Id, run)) throw new InvalidOperationException("Attendez la fin de la réponse / Wait for the response to finish.");
        using var registration = lifetime.Register(run.Cancellation.Cancel);
        var ct = run.Cancellation.Token;
        string error = "", status = "";
        try
        {
            await emit(new { @event = "status", chatId = chat.Id, text = "Compactage manuel… / Compacting…" });
            var secret = await Decrypt(provider.ProtectedKey, ct);
            var changed = await ContextDetails.CompactAsync(run, async (text, token) => {
                Completion result;
                if (provider.IsOpenCode)
                {
                    var directory = OpenCodeDirectory(project);
                    await EnsureOpenCode(provider, secret, directory, token);
                    var engine = new OpenCodeEngine(http);
                    var isolated = new Provider { Kind = provider.Kind, BaseUrl = provider.BaseUrl, Username = provider.Username, Model = provider.Model, OpenCodeTools = false };
                    var session = await engine.CreateSessionAsync(isolated, secret, directory, "Compactage manuel", token);
                    result = await engine.PromptAsync(isolated, secret, directory, session, text, ContextDetails.SummaryInstruction, [], _ => { }, token);
                }
                else result = await new ChatEngine(http).StreamAsync(provider, secret, new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = ContextDetails.SummaryInstruction },
                    new JsonObject { ["role"] = "user", ["content"] = text }), [], _ => { }, token);
                return result.Message["content"]?.GetValue<string>() ?? "";
            }, ct);
            status = changed ? "Contexte compacté / Context compacted" : "Aucune réduction utile : historique conservé / No useful reduction: history preserved";
            return new { changed, details = await ReadContext(chat.Id, provider.Id, ct) };
        }
        catch (Exception ex) { error = ex is OperationCanceledException ? "Compactage arrêté / Compaction stopped" : ex.Message; throw; }
        finally { runs.TryRemove(chat.Id, out _); await emit(new { @event = "done", chatId = chat.Id, error, status }); if(error.Length==0&&!ct.IsCancellationRequested)await SendNext(chat.Id,lifetime); }
    }
}

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;

namespace OhMyHarness.Core.Hosting;

public sealed partial class HarnessService
{
    sealed record PendingQuestion(int ChatId, IReadOnlyList<AgentQuestion> Questions, TaskCompletionSource<AgentAnswer> Completion);
    readonly ConcurrentDictionary<string, PendingQuestion> questions = new();
    WorkflowTools CreateWorkflow(ConversationSession run) => new(
        async (items, ct) =>
        {
            var id = Guid.NewGuid().ToString("N");
            var pending = new PendingQuestion(run.Chat.Id, items, new(TaskCreationOptions.RunContinuationsAsynchronously));
            questions[id] = pending;
            try
            {
                await emit(new { @event = "question", id, chatId = run.Chat.Id, questions = items });
                await emit(new { @event = "status", chatId = run.Chat.Id, text = "Réponse attendue / Waiting for your answer" });
                var answer = await pending.Completion.Task.WaitAsync(ct);
                await using var db = Db();
                var message = new Message { ChatId = run.Chat.Id, Role = "interaction", State = "ui", Content = QuestionSummary(items, answer) };
                db.Messages.Add(message); await db.SaveChangesAsync(ct);
                await emit(new { @event = "message", chatId = run.Chat.Id, message = MessageView(message) });
                await emit(new { @event = "status", chatId = run.Chat.Id, text = "Reprise du travail… / Resuming…" });
                return answer;
            }
            finally
            {
                questions.TryRemove(id, out _);
                await emit(new { @event = "question.closed", id, chatId = run.Chat.Id });
            }
        },
        async (items, ct) =>
        {
            await using var db = Db();
            var message = await db.Messages.FirstOrDefaultAsync(x => x.ChatId == run.Chat.Id && x.Role == "tasks", ct);
            if (message == null) { message = new() { ChatId = run.Chat.Id, Role = "tasks", State = "ui" }; db.Messages.Add(message); }
            message.Content = items.ToJsonString(); await db.SaveChangesAsync(ct);
            await emit(new { @event = "message", chatId = run.Chat.Id, message = MessageView(message) });
        });
    static string QuestionSummary(IReadOnlyList<AgentQuestion> items, AgentAnswer answer) =>
        string.Join("\n\n", items.Select((q, i) => q.Question + "\n→ " + (answer.Cancelled ? "Annulé / Cancelled" : string.Join(", ", answer.Answers[i]))));
    object AnswerQuestion(JsonObject p)
    {
        if (!questions.TryGetValue(S(p, "id"), out var pending) || pending.ChatId != I(p, "chatId")) throw new InvalidOperationException("Question expirée / Question expired.");
        var answer = JsonSerializer.Deserialize<AgentAnswer>(p.ToJsonString(), Json) ?? throw new ArgumentException("Answer required.");
        WorkflowTools.ValidateAnswer(pending.Questions, answer);
        if (!pending.Completion.TrySetResult(answer)) throw new InvalidOperationException("Réponse déjà envoyée / Already answered.");
        return true;
    }
}

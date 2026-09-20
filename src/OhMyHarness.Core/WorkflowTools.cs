using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public sealed record AgentOption(string Label, string Description);
public sealed record AgentQuestion(string Question, IReadOnlyList<AgentOption> Options, bool Multiple, bool Custom);
public sealed record AgentAnswer(bool Cancelled, IReadOnlyList<IReadOnlyList<string>> Answers);

/// <summary>User decisions are never permission grants and must never be auto-accepted.</summary>
public sealed class WorkflowTools(Func<IReadOnlyList<AgentQuestion>, CancellationToken, Task<AgentAnswer>> ask,
    Func<JsonArray, CancellationToken, Task> saveTasks)
{
    public const string Instructions = "\nUse todowrite to maintain a structured task list for multi-step work (pending, in_progress, completed, cancelled). Update it as work progresses; never mark unperformed work completed. Use question to ask the user for necessary decisions with options or a free-text form. The tool waits for the user's actual answer. Cancellation is not approval. Avoid repeating identical tool calls without new information.";
    public static bool Handles(string name) => name is "todowrite" or "question";
    public static void AddDefinitions(JsonArray definitions, bool child = false)
    {
        if (!child) definitions.Add(JsonNode.Parse("""
        {"type":"function","function":{"name":"todowrite","description":"Replace the complete structured task list for this conversation. Keep completed tasks; send an empty list to clear.","parameters":{"type":"object","properties":{"todos":{"type":"array","maxItems":50,"items":{"type":"object","properties":{"content":{"type":"string"},"status":{"type":"string","enum":["pending","in_progress","completed","cancelled"]}},"required":["content","status"],"additionalProperties":false}}},"required":["todos"],"additionalProperties":false}}}
        """));
        definitions.Add(JsonNode.Parse("""
        {"type":"function","function":{"name":"question","description":"Wait for the user to answer 1 to 8 questions. Options can be empty for a free-text form. custom defaults to true. Do not use this as a substitute for tool access permissions.","parameters":{"type":"object","properties":{"questions":{"type":"array","minItems":1,"maxItems":8,"items":{"type":"object","properties":{"question":{"type":"string"},"options":{"type":"array","maxItems":12,"items":{"type":"object","properties":{"label":{"type":"string"},"description":{"type":"string"}},"required":["label","description"],"additionalProperties":false}},"multiple":{"type":"boolean"},"custom":{"type":"boolean"}},"required":["question","options"],"additionalProperties":false}}},"required":["questions"],"additionalProperties":false}}}
        """));
    }
    public static JsonArray ValidateTasks(JsonArray items)
    {
        if (items.Count > 50) throw new ArgumentException("50 tâches maximum / Maximum 50 tasks.");
        var result = new JsonArray();
        foreach (var item in items)
        {
            var content = item?["content"]?.GetValue<string>()?.Trim() ?? "";
            var status = item?["status"]?.GetValue<string>();
            if (content.Length is < 1 or > 1000 || status is not ("pending" or "in_progress" or "completed" or "cancelled"))
                throw new ArgumentException("Tâche ou statut invalide / Invalid task or status.");
            result.Add(new JsonObject { ["content"] = content, ["status"] = status });
        }
        return result;
    }
    public static IReadOnlyList<AgentQuestion> ParseQuestions(JsonArray items)
    {
        if (items.Count is < 1 or > 8) throw new ArgumentException("1 à 8 questions requises.");
        return items.Select(item => {
            var title = item?["question"]?.GetValue<string>()?.Trim() ?? "";
            var options = (item?["options"] as JsonArray ?? []).Select(x => new AgentOption(x?["label"]?.GetValue<string>() ?? "", x?["description"]?.GetValue<string>() ?? "")).ToList();
            var custom = item?["custom"]?.GetValue<bool>() ?? true;
            if (title.Length is < 1 or > 4000 || options.Count > 12 || options.Any(x => x.Label.Length is < 1 or > 200 || x.Description.Length > 1000) || options.Select(x => x.Label).Distinct().Count() != options.Count || options.Count == 0 && !custom)
                throw new ArgumentException("Question invalide / Invalid question.");
            return new AgentQuestion(title, options, item?["multiple"]?.GetValue<bool>() ?? false, custom);
        }).ToList();
    }
    public static void ValidateAnswer(IReadOnlyList<AgentQuestion> questions, AgentAnswer answer)
    {
        if (answer.Cancelled) return;
        if (answer.Answers == null || answer.Answers.Count != questions.Count) throw new ArgumentException("Répondez à chaque question / Answer every question.");
        for (int i = 0; i < questions.Count; i++)
        {
            var values = answer.Answers[i]; var q = questions[i];
            if (values == null || values.Count == 0 || values.Count > 13 || !q.Multiple && values.Count != 1 || values.Distinct().Count() != values.Count || values.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 4000 || !q.Custom && !q.Options.Any(o => o.Label == x)))
                throw new ArgumentException("Réponse invalide / Invalid answer.");
        }
    }
    public async Task<AgentAnswer> AskAsync(IReadOnlyList<AgentQuestion> questions, CancellationToken ct)
    {
        var answer = await ask(questions, ct); ct.ThrowIfCancellationRequested(); ValidateAnswer(questions, answer); return answer;
    }
    public async Task<string> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        if (name == "todowrite")
        {
            var tasks = ValidateTasks(args["todos"] as JsonArray ?? throw new ArgumentException("todos requis"));
            await saveTasks(tasks, ct); return tasks.ToJsonString();
        }
        var answer = await AskAsync(ParseQuestions(args["questions"] as JsonArray ?? throw new ArgumentException("questions requis")), ct);
        return System.Text.Json.JsonSerializer.Serialize(answer, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
    public WorkflowTools ForChild() => new(AskAsync, (_, _) => Task.CompletedTask);
    public Task SaveTasksAsync(JsonArray tasks, CancellationToken ct) => saveTasks(ValidateTasks(tasks), ct);
    public async Task<bool> DecideLoopAsync(string details, CancellationToken ct)
    {
        var result = await AskAsync([new("Boucle détectée / Repeated calls\n" + details[..Math.Min(3000, details.Length)] + "\nContinuer cet appel ou arrêter le travail concerné ? / Continue this call or stop this work?",
            [new("Arrêter / Stop", "Arrêter le travail concerné / Stop affected work"), new("Continuer une fois / Continue once", "Autoriser cet appel uniquement / Allow only this call")], false, false)], ct);
        return !result.Cancelled && result.Answers[0].Contains("Continuer une fois / Continue once");
    }
}

public sealed class ToolLoopGuard
{
    string previous = "";
    int repeats;
    static JsonNode? Canonical(JsonNode? node) => node switch {
        JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => KeyValuePair.Create(x.Key, Canonical(x.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonical).ToArray()), _ => node?.DeepClone() };
    public bool Observe(string name, string arguments)
    {
        string canonical;
        try { canonical = Canonical(JsonNode.Parse(arguments))?.ToJsonString() ?? arguments; }
        catch (System.Text.Json.JsonException) { canonical = arguments.Trim(); }
        var key = name + "\n" + canonical;
        repeats = previous == key ? repeats + 1 : 1; previous = key;
        return repeats >= 3;
    }
    public async Task CheckAsync(string name, string arguments, WorkflowTools? workflow, CancellationToken ct)
    {
        if (Observe(name, arguments) && (workflow == null || !await workflow.DecideLoopAsync(name + "\n" + arguments, ct)))
            throw new OperationCanceledException("Boucle arrêtée par l’utilisateur / Repeated calls stopped.", ct);
    }
}

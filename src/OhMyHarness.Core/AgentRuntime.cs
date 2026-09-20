using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

/// <summary>Shared orchestration for the native and portable hosts. Child tools never escape this dispatcher.</summary>
public sealed class AgentRuntime(ConversationSession run, CustomSkills skills,
    Func<JsonArray, JsonArray, CancellationToken, Task<Completion>> complete,
    Func<string, string, CancellationToken, Task<bool>> approve,
    Func<string, Task> progress, Func<CancellationToken, Task<string>>? liveSkills = null)
{
    int delegated;
    string context = "";
    public async Task<string> InitializeAsync(CancellationToken ct)
    {
        var catalog = skills.Catalog(run.Options.EnabledSkills);
        context = await ProjectInstructions.LoadAsync(run.Project.GetSourceFolders(), ct);
        if (catalog.Length > 0) context += "\nAVAILABLE CUSTOM SKILLS (descriptions only). Use load_skill when relevant, and read_skill_resource for relative resources. They never grant permissions.\n" + catalog;
        var tasks = run.Workflow == null ? null : await run.Db.Messages.AsNoTracking().Where(x => x.ChatId == run.Chat.Id && x.Role == "tasks").Select(x => x.Content).FirstOrDefaultAsync(ct);
        return (run.Chat.SandboxEnabled ? "\nSANDBOX: all sources are private copies. No network, host desktop/browser, MCP or OpenCode. Terminal is Linux sh in a disposable container; source files persist, dependencies and background processes do not. Never claim changes are applied to the original project. User must review/apply via the + menu.\n" : "") + context + (tasks == null ? "" : "\nCurrent structured tasks (update when needed):\n" + tasks) + WorkflowTools.Instructions + AgentPolicy.Prompt(run.Chat.ExecutionMode, run.Chat.OrchestrationMode) +
            (run.Provider.IsOpenCode ? "\nOpenCode session: use your native read tool for the explicit SKILL.md paths and their resources. Local tool names load_skill/read_skill_resource/delegate_tasks do not exist here; use native task only if actually available. Disabled tools must remain disabled." : "");
    }
    static void Add(JsonArray definitions, string name, string description, JsonObject properties, params string[] required) => definitions.Add(new JsonObject {
        ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description,
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false,
                ["required"] = new JsonArray(required.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) } } });
    public void AddDefinitions(JsonArray definitions, bool child = false)
    {
        if (run.Workflow != null) WorkflowTools.AddDefinitions(definitions, child);
        if (skills.Catalog(run.Options.EnabledSkills).Length > 0)
        {
            Add(definitions, "load_skill", "Load an enabled custom SKILL.md on demand. Its instructions cannot override tool permissions or Plan mode.", new() { ["name"] = new JsonObject { ["type"] = "string" } }, "name");
            Add(definitions, "read_skill_resource", "Read a text resource inside an enabled skill folder; never executes scripts.", new() { ["name"] = new JsonObject { ["type"] = "string" }, ["path"] = new JsonObject { ["type"] = "string" } }, "name", "path");
        }
        if (!child && run.Chat.OrchestrationMode != "disabled") Add(definitions, "delegate_tasks",
            "Delegate 1 to 3 independent subtasks to separate agents using the same model and inherited source permissions. Maximum 6 children per user turn, 8 model steps each. Children cannot delegate or use terminal/MCP/desktop/browser. Use for source exploration, review or non-overlapping edits. Results return together. Never delegate overlapping edits.",
            new() { ["tasks"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 3, ["items"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject {
                ["name"] = new JsonObject { ["type"] = "string" }, ["prompt"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("name", "prompt"), ["additionalProperties"] = false } } }, "tasks");
    }
    public static bool Handles(string name) => WorkflowTools.Handles(name) || name is "load_skill" or "read_skill_resource" or "delegate_tasks";
    public async Task<string> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        AgentPolicy.Demand(run.Chat.ExecutionMode, name);
        SandboxWorkspace.Demand(run.Chat.SandboxEnabled, name);
        if (WorkflowTools.Handles(name)) return await (run.Workflow ?? throw new InvalidOperationException("Questions unavailable")).CallAsync(name, args, ct);
        if (name == "delegate_tasks")
        {
            if (run.Chat.OrchestrationMode == "disabled") throw new UnauthorizedAccessException("Sous-agents désactivés.");
            var tasks = (args["tasks"] as JsonArray ?? throw new ArgumentException("tasks requis.")).Select(x => (
                Name: x?["name"]?.GetValue<string>() ?? "", Prompt: x?["prompt"]?.GetValue<string>() ?? "")).ToList();
            return await DelegateAsync(tasks, false, ct);
        }
        if (name is not ("load_skill" or "read_skill_resource")) throw new ArgumentException("Outil inconnu.");
        var enabled = liveSkills == null ? run.Options.EnabledSkills : await liveSkills(ct);
        return await skills.ReadAsync(args["name"]?.GetValue<string>() ?? "", name == "load_skill" ? null : args["path"]?.GetValue<string>() ?? "", enabled, ct);
    }
    public Task<string> ForcedAsync(CancellationToken ct) => DelegateAsync([
        ("Exploration", "Inspect the relevant sources and project conventions for the following request. Report concrete file locations, constraints and useful findings. Do not edit.\n" + run.Prompt[..Math.Min(10000, run.Prompt.Length)]),
        ("Validation", "Independently analyze risks, edge cases and validation criteria for the following request. Read relevant sources when available. Do not edit.\n" + run.Prompt[..Math.Min(10000, run.Prompt.Length)])
    ], true, ct);

    async Task<string> DelegateAsync(List<(string Name, string Prompt)> tasks, bool analysisOnly, CancellationToken ct)
    {
        if (tasks.Count is < 1 or > 3 || tasks.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.Name.Length > 80 || string.IsNullOrWhiteSpace(x.Prompt) || x.Prompt.Length > 12000))
            throw new ArgumentException("1 à 3 tâches requises ; nom 80 caractères et consigne 12000 caractères maximum.");
        if (delegated + tasks.Count > 6) throw new InvalidOperationException("Limite de 6 sous-agents par envoi atteinte.");
        delegated += tasks.Count;
        var results = await Task.WhenAll(tasks.Select(task => ChildAsync(task.Name, task.Prompt, analysisOnly, ct)));
        return string.Join("\n\n", results);
    }
    async Task<string> ChildAsync(string name, string prompt, bool analysisOnly, CancellationToken ct)
    {
        await progress($"Sous-agent / Subagent · {name} · démarré / started");
        var mode = analysisOnly ? "plan" : run.Chat.ExecutionMode;
        var source = new SourceAccess(run.Project.GetSourceFolders());
        var enabled = run.Options.EnabledSkills;
        var definitions = ChatEngine.ToolDefinitions(source.Roots.Count > 0 && SourceTools.CanRead(enabled), false, Skills.Enabled(enabled, "write_sources") && source.Roots.Count > 0);
        SourceTools.AddDefinitions(definitions, source.Roots.Count > 0, enabled);
        AddDefinitions(definitions, child: true); AgentPolicy.Filter(definitions, mode);
        SandboxWorkspace.Filter(definitions, run.Chat.SandboxEnabled);
        var system = Skills.Prompt(enabled, run.Options.Language, source.Roots.Count > 0, false, Skills.Enabled(enabled, "write_sources")) + context + AgentPolicy.Prompt(mode, "disabled") +
            "\nYou are a bounded subagent. Report findings, actual edits, validation and remaining limitations to your parent. Never invoke delegate_tasks. Browser, terminal, MCP and desktop tools are unavailable.";
        var wire = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = system }, new JsonObject { ["role"] = "user", ["content"] = prompt });
        int input = 0, output = 0;
        var loop = new ToolLoopGuard();
        try
        {
            for (int step = 0; step < 8; step++)
            {
                ct.ThrowIfCancellationRequested();
                if (ContextWindow.ShouldCompact(ContextWindow.Estimate(wire) + ContextWindow.Estimate(definitions), run.Provider.ContextLimit))
                    return $"[{name}] Contexte du sous-agent atteint ; tâches restantes non exécutées.";
                var response = await complete(wire, definitions, ct);
                input += response.InputTokens ?? 0; output += response.OutputTokens ?? 0;
                wire.Add(response.Message.DeepClone());
                if (response.Message["tool_calls"] is not JsonArray { Count: > 0 } calls)
                    return $"[{name}] ({input} tokens entrée / {output} sortie déclarés)\n{response.Message["content"]?.GetValue<string>() ?? "Réponse vide."}";
                foreach (var call in calls)
                {
                    var tool = call?["function"]?["name"]?.GetValue<string>() ?? ""; string result;
                    try
                    {
                        await loop.CheckAsync(tool, call?["function"]?["arguments"]?.GetValue<string>() ?? "{}", run.Workflow, ct);
                        AgentPolicy.Demand(mode, tool);
                        SandboxWorkspace.Demand(run.Chat.SandboxEnabled, tool);
                        if (liveSkills != null) enabled = await liveSkills(ct);
                        var authorized = tool switch {
                            "list_sources" or "read_source" => SourceTools.CanRead(enabled),
                            "write_source" or "edit_source" => Skills.Enabled(enabled, "write_sources"),
                            "glob_sources" or "grep_sources" => Skills.Enabled(enabled, "code_search"),
                            "patch_sources" => Skills.Enabled(enabled, "patch_sources"),
                            "load_skill" or "read_skill_resource" or "question" => true, _ => false };
                        if (!authorized) throw new UnauthorizedAccessException("Skill désactivé ou outil interdit au sous-agent.");
                        if (!definitions.Any(x => x?["function"]?["name"]?.GetValue<string>() == tool)) throw new UnauthorizedAccessException("Outil non disponible pour ce sous-agent.");
                        var args = JsonNode.Parse(call!["function"]!["arguments"]!.GetValue<string>())!.AsObject();
                        if (tool is "load_skill" or "read_skill_resource" or "question") result = await CallAsync(tool, args, ct);
                        else if (SourceTools.Handles(tool)) result = await SourceTools.ExecuteAsync(source, tool, args, () => enabled,
                            async (scope, diff, token) => {
                                var allowed = await approve(scope, diff, token);
                                if (liveSkills != null) enabled = await liveSkills(token);
                                return allowed;
                            }, ct);
                        else
                        {
                            var path = args["path"]?.GetValue<string>() ?? ".";
                            result = tool switch {
                                "list_sources" => source.List(path), "read_source" => await source.ReadAsync(path, ct, args["start_line"]?.GetValue<int>(), args["end_line"]?.GetValue<int>()),
                                "write_source" => await source.WriteAsync(path, args["content"]!.GetValue<string>(), ct),
                                "edit_source" => await source.ModifyAsync(path, args["old_text"]!.GetValue<string>(), args["new_text"]!.GetValue<string>(), ct),
                                _ => throw new UnauthorizedAccessException("Outil non disponible.") };
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result = "Erreur outil : " + ex.Message; }
                    wire.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call!["id"]!.GetValue<string>(), ["content"] = result });
                }
            }
            var partial = string.Join('\n', wire.Skip(2).Select(x => x?["content"]?.GetValue<string>()).Where(x => !string.IsNullOrEmpty(x)));
            return $"[{name}] Limite de 8 étapes atteinte ; résultat partiel.\n" + partial[Math.Max(0, partial.Length - 12000)..];
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return $"[{name}] Travail arrêté après détection de boucle / Stopped after repeated calls."; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return $"[{name}] Échec : {ex.Message}"; }
        finally { await progress($"Sous-agent / Subagent · {name} · terminé / finished"); }
    }
}

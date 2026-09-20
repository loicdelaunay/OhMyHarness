using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    AgentRuntime CreateAgentRuntime(ConversationRun run, string secret) => new(run, new CustomSkills(CustomSkills.DefaultRoot),
        async (wire, definitions, ct) =>
        {
            if (!run.Provider.IsOpenCode) return await engine.StreamAsync(run.Provider, secret, wire, definitions, _ => { }, ct, run.Options.ThinkingLevel);
            var directory = OpenCodeDirectory(run.Project);
            await EnsureOpenCodeServerAsync(run.Provider, secret, ct, run.Project);
            var session = await openCodeEngine.CreateSessionAsync(run.Provider, secret, directory, "Sous-agent · " + run.Chat.Title, ct);
            return await openCodeEngine.PromptAsync(run.Provider, secret, directory, session, wire.Last()?["content"]?.GetValue<string>() ?? "",
                wire[0]?["content"]?.GetValue<string>() ?? "", [], _ => { }, ct, policy: new("plan", "disabled"), workflow: run.Workflow?.ForChild());
        },
        (scope, diff, ct) => RequestAccessAsync(scope, "Sous-agent · Patch", diff, "Patch des sources", ct),
        text => { SetRunStatus(run, text); return Task.CompletedTask; },
        _ => Task.FromResult(state.EnabledSkills));
}

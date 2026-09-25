using System.Text.Json.Nodes;
using OhMyHarness.Core;

static class CompleteDesignChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(Skills.All.Count(x => x.Id == CompleteDesignSkill.Id) == 1,
            "Complete design appears once in the shared built-in catalog");
        foreach (var language in new[] { "fr", "en" })
        {
            string baseline = Skills.Prompt("planning", language);
            check(!baseline.Contains(CompleteDesignSkill.Instructions), "Complete design remains opt-in: " + language);
            string enabled = Skills.Prompt("planning," + CompleteDesignSkill.Id, language);
            check(enabled.Contains(CompleteDesignSkill.Instructions) && enabled.Contains(Skills.All.Single(x => x.Id == "planning").Instruction),
                "Complete design composes with existing skills: " + language);
            check(Skills.Prompt("planning,complete_design_other", language) == baseline,
                "Only an exact skill ID activates complete design: " + language);
        }

        string root = Path.Combine(Path.GetTempPath(), "omh-design-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (string mode in new[] { "plan", "execute" })
                foreach (string orchestration in new[] { "disabled", "auto" })
                {
                    using var run = new ConversationSession(new Chat { Id = 1, ExecutionMode = mode, OrchestrationMode = orchestration },
                        new Project(), new Provider(), new AppState { EnabledSkills = CompleteDesignSkill.Id }, "Design a project", [], Path.Combine(root, "test.sqlite"));
                    var runtime = new AgentRuntime(run, new CustomSkills(Path.Combine(root, "skills")),
                        (_, _, _) => throw new Exception("No model call expected"), (_, _, _) => Task.FromResult(false), _ => Task.CompletedTask);
                    string prompt = await runtime.InitializeAsync(default);
                    check(prompt.Contains(AgentPolicy.Prompt(mode, orchestration)), "Design preserves runtime policy: " + mode + "/" + orchestration);
                    var definitions = new JsonArray();
                    runtime.AddDefinitions(definitions);
                    var names = definitions.Select(x => x!["function"]!["name"]!.GetValue<string>()).ToArray();
                    check(names.SequenceEqual(orchestration == "disabled" ? Array.Empty<string>() : new[] { "delegate_tasks" }),
                        "Design grants no tools and respects orchestration: " + mode + "/" + orchestration);
                    var childDefinitions = new JsonArray();
                    runtime.AddDefinitions(childDefinitions, child: true);
                    check(childDefinitions.Count == 0, "Design does not grant recursive child delegation");
                }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}

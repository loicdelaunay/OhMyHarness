using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;

static class ResponseStyleChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(FeatureSettings.Read("{}").ResponseStyle == "default" && ResponseStyles.Prompt("default") == "",
            "Response style: older settings retain the unmodified default prompt");
        check(ResponseStyles.Prompt("unknown") == "" && ResponseStyles.Prompt(null) == "",
            "Response style: unknown or null values fall back without injecting text");
        string root = Path.Combine(Path.GetTempPath(), "omh-style-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string database = Path.Combine(root, "database.sqlite");
            await using (var db = new HarnessDb(database))
            {
                await db.InitializeAsync();
                var saved = await db.States.SingleAsync();
                saved.FeaturesJson = new FeatureSettings { ResponseStyle = "detailed", Theme = "fly-dark" }.Json();
                await db.SaveChangesAsync();
            }
            await using (var db = new HarnessDb(database))
            {
                var saved = FeatureSettings.Read((await db.States.AsNoTracking().SingleAsync()).FeaturesJson);
                check(saved.ResponseStyle == "detailed" && saved.Theme == "fly-dark", "Response style persists beside existing GUI/CLI settings without a migration");
            }
            foreach (var provider in new[] { new Provider { Kind = "openai" }, new Provider { Kind = "opencode" } })
                foreach (var style in ResponseStyles.All)
                {
                    var options = new AppState { FeaturesJson = new FeatureSettings { ResponseStyle = style.Id }.Json(), EnabledSkills = "" };
                    using var run = new ConversationSession(new Chat { Id = 100 }, new Project(), provider, options, "test", [], database);
                    options.FeaturesJson = new FeatureSettings { ResponseStyle = "fun" }.Json();
                    var runtime = new AgentRuntime(run, new CustomSkills(Path.Combine(root, "skills")),
                        (_, _, _) => throw new Exception("No model call expected"), (_, _, _) => Task.FromResult(false), _ => Task.CompletedTask);
                    string prompt = await runtime.InitializeAsync(default);
                    check(style.Id == "default" ? !prompt.Contains("RESPONSE STYLE:") : prompt.Contains(style.Instruction),
                        $"Response style captured in shared runtime: {provider.Kind}/{style.Id}");
                    check(prompt.Contains(AgentPolicy.Prompt(run.Chat.ExecutionMode, run.Chat.OrchestrationMode)), "Response style preserves agent permissions and mode instructions");
                }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}

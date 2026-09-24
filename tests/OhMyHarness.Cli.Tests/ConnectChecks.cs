using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using OhMyHarness.Cli;
using OhMyHarness.Core;

static class ConnectChecks
{
    sealed class ModelApi(bool fail = false) : HttpMessageHandler
    {
        public int Calls;
        public string? Path, Authorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Path = request.RequestUri!.AbsolutePath; Authorization = request.Headers.Authorization?.ToString();
            string body = request.RequestUri.AbsolutePath == "/provider"
                ? """{"all":[{"id":"demo","models":{"chat":{"id":"chat","name":"Chat"}}}],"connected":["demo"]}"""
                : """{"data":[{"id":"deepseek-reasoner"},{"id":"deepseek-chat"}]}""";
            return Task.FromResult(new HttpResponseMessage(fail ? HttpStatusCode.Unauthorized : HttpStatusCode.OK) { Content = new StringContent(fail ? "sensitive-server-body" : body, Encoding.UTF8, "application/json") });
        }
    }
    public static async Task Run(Action<bool, string> check)
    {
        check(ProviderConnectionWizard.Presets.Any(p => p.Id == "deepseek") && ProviderConnectionWizard.Presets.Any(p => p.Id == "compatible") && ProviderConnectionWizard.Presets.Any(p => p.Id == "opencode"), "Connect offers DeepSeek, compatible APIs and OpenCode");
        async Task<(Provider? Draft, List<ConnectionStep> Steps, ModelApi Api)> Scenario(string?[] answers, bool fail = false)
        {
            var queue = new Queue<string?>(answers); var steps = new List<ConnectionStep>(); var api = new ModelApi(fail);
            using var http = new HttpClient(api); Provider? saved = null;
            var wizard = new ProviderConnectionWizard(http, step => { steps.Add(step); if (queue.Count == 0) throw new Exception("Unexpected prompt: " + step.Title); return Task.FromResult(queue.Dequeue()); },
                (draft, key, ct) => { saved = draft; check(key == "fixture-key" || key == "", "Connect passes credentials only to final persistence"); return Task.FromResult(42); });
            await wizard.RunAsync(default);
            check(steps.All(s => !s.Title.Contains("fixture-key") && !s.Body.Contains("fixture-key") && !s.Body.Contains("sensitive-server-body")), "Connect masks credentials and server bodies in confirmation/errors");
            return (saved, steps, api);
        }
        var auto = await Scenario(["deepseek", "fixture-key", "https://api.deepseek.com", "auto", "deepseek-chat", "DeepSeek travail", "save"]);
        check(auto.Steps[0].Title.StartsWith("1/4") && auto.Steps[1].Secret && auto.Api.Path == "/models" && auto.Api.Authorization == "Bearer fixture-key", "Connect asks type then masked key and uses correct discovery endpoint");
        check(auto.Draft is { Name: "DeepSeek travail", Model: "deepseek-chat" } && ProviderModels.Visible(auto.Draft).Count == 2, "Discovered models and default are committed together");
        var manual = await Scenario(["local", "", "http://localhost:1234/v1", "manual", "local-a, local-b,local-a", "local-b", "Local", "save"]);
        check(manual.Api.Calls == 0 && manual.Draft?.Model == "local-b" && ProviderModels.Visible(manual.Draft).Count == 2, "Manual local connection deduplicates models without any API call");
        var fallback = await Scenario(["deepseek", "fixture-key", "https://api.deepseek.com", "auto", "manual", "deepseek-chat", "DeepSeek", "save"], true);
        check(fallback.Api.Calls == 1 && fallback.Draft?.Model == "deepseek-chat", "Discovery failure allows manual model entry before saving");
        var cancelled = await Scenario(["deepseek", "fixture-key", "https://api.deepseek.com", "manual", "deepseek-chat", "DeepSeek", "cancel"]);
        check(cancelled.Draft == null, "Cancelling final confirmation saves no provider");
        var early = await Scenario(["deepseek", null]);
        check(early.Draft == null && early.Api.Calls == 0, "Cancelling credentials saves nothing and makes no request");
        var opencode = await Scenario(["opencode", "fixture-key", "http://localhost:4096", "opencode", "auto", "OpenCode", "save"]);
        check(opencode.Api.Path == "/provider" && opencode.Api.Authorization?.StartsWith("Basic ") == true && opencode.Draft is { Kind: "opencode", Model: "demo/chat" }, "OpenCode uses its native catalog and authentication");
        foreach (var size in new[] { (56, 18), (80, 24), (118, 36), (180, 50) })
        {
            foreach (bool commands in new[] { false, true })
            {
                string frame = Regex.Replace(TerminalUi.DemoFrame(width: size.Item1, height: size.Item2, commands: commands), "\x1b\\[[0-9;]*m", "");
                var lines = frame.TrimEnd('\r', '\n').Split('\n');
                check(lines.Length == size.Item2 && lines.All(l => TerminalText.Width(l.TrimEnd('\r')) == size.Item1), $"Minimal layout fits {size}, commands={commands}");
                check(frame.Contains("OhMyHarness CLI") && !frame.Contains("╭") && !frame.Contains("╔") && !frame.Contains("TERMINAL WORKSPACE"), "Minimal layout has no sidebar or panel boxes");
            }
        }
        var menu = Regex.Replace(TerminalUi.DemoFrame(commands: true), "\x1b\\[[0-9;]*m", "");
        check(menu.Contains("/connect") && menu.Contains("Ajouter un fournisseur"), "Command menu shows command names alongside their descriptions");
    }
}

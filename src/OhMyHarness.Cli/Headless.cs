using System.Text.Json.Nodes;
using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public static class Headless
{
    public static async Task<int> RunAsync(CliOptions options)
    {
        if (options.Prompt.Length == 0 && Console.IsInputRedirected) options.Prompt = await Console.In.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(options.Prompt)) throw new ArgumentException("Prompt required: omh run \"your request\"");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        CliClient? client = null;
        bool questionRequired = false;
        var outputLock = new object();
        var written = new Dictionary<int, string>();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            client = new(options, (name, _, _) => name == "permission"
                ? Task.FromResult<JsonNode?>(JsonValue.Create("deny"))
                : throw new NotSupportedException("Host operation unavailable in CLI: " + name), value =>
            {
                var node = CliClient.J(value);
                lock (outputLock)
                {
                    string kind = node["event"]?.GetValue<string>() ?? "";
                    if (kind == "started") timer.Restart();
                    if (kind == "done") { node["durationSeconds"] = timer.Elapsed.TotalSeconds; if (!options.Json) Console.Error.WriteLine($"Durée / Duration: {timer.Elapsed.TotalSeconds:0.#} s"); }
                    if (options.Json)
                    {
                        node.Remove("html"); if (node["message"] is JsonObject message) message.Remove("html");
                        Console.WriteLine(node.ToJsonString());
                    }
                    else if (kind == "stream")
                    {
                        int id = node["messageId"]!.GetValue<int>(); string text = node["text"]?.GetValue<string>() ?? "";
                        var previous = written.GetValueOrDefault(id, "");
                        if (text.StartsWith(previous, StringComparison.Ordinal)) Console.Write(TerminalText.Clean(text[previous.Length..]));
                        written[id] = text;
                    }
                    else if (kind == "message" && node["message"]?["role"]?.GetValue<string>() == "assistant")
                    {
                        var message = node["message"]!; int id = message["id"]!.GetValue<int>(); string text = message["content"]?.GetValue<string>() ?? "";
                        if (!written.ContainsKey(id)) { Console.WriteLine(TerminalText.Clean(text)); written[id] = text; }
                    }
                    else if (kind == "status") Console.Error.WriteLine(TerminalText.Clean(node["text"]?.GetValue<string>() ?? ""));
                    if (kind == "question") { questionRequired = true; cancellation.Cancel(); }
                }
                return Task.CompletedTask;
            });
            var initial = await client.Initialize(options, cancellation.Token);
            if (initial.ProviderId == 0) throw new InvalidOperationException("Configure a provider in interactive mode with /connect.");
            await client.Call("send", new { chatId = initial.ChatId, providerId = initial.ProviderId, text = options.Prompt }, cancellation.Token);
            if (!options.Json) Console.WriteLine();
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(questionRequired ? "An agent question needs an interactive session. Resume with --chat." : "Cancelled.");
            return questionRequired ? 3 : 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            if (client != null) await client.DisposeAsync();
        }
    }
}

using OhMyHarness.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Cli;

public sealed partial class TerminalUi
{
    record DisplayLine(string Text, string Kind, int QueueId = 0);
    readonly Dictionary<(int Chat, int Message), (Message Item, string Reasoning, int Width, bool Details, List<DisplayLine> Lines)> lineCache = [];
    List<DisplayLine> Transcript(ChatView view, int width)
    {
        var lines = new List<DisplayLine>();
        foreach (var message in view.Messages.Values.Where(m => m.Role != "tasks"))
        {
            string reasoning = view.Reasoning.GetValueOrDefault(message.Id, "");
            var key = (message.ChatId, message.Id);
            if (!lineCache.TryGetValue(key, out var cached) || !ReferenceEquals(cached.Item, message) || cached.Width != width || cached.Details != showDetails || cached.Reasoning != reasoning)
            {
                var rendered = new List<DisplayLine>();
                string kind = message.Role == "user" ? "user" : message.Role == "tool" ? "tool" : "assistant";
                if (kind == "tool") rendered.Add(new("● " + message.Content.Split('\n')[0], "tool-title"));
                if (reasoning.Length > 0)
                {
                    rendered.Add(new((showDetails ? "▾ " : "▸ ") + L("Raisonnement", "Reasoning") + $" · {reasoning.Length:N0} · /details", "muted"));
                    if (showDetails) rendered.AddRange(TerminalText.Wrap(reasoning, width).Select(s => new DisplayLine(s, "muted")));
                }
                string content = message.Role == "tool" && !showDetails ? string.Join('\n', message.Content.Split('\n').Skip(1).Take(2)) : message.Content;
                if (kind == "user") content = "> " + content;
                bool code = false;
                foreach (var source in content.Split('\n'))
                {
                    if (source.TrimStart().StartsWith("```")) { code = !code; rendered.Add(new(code ? "┌ " + source.Trim()[3..] : "└", "muted")); continue; }
                    string lineKind = code ? "code" : kind;
                    if (source.StartsWith('#')) lineKind = "heading";
                    rendered.AddRange(TerminalText.Wrap(!code && source.StartsWith('#') ? source.TrimStart('#').TrimStart() : source, width).Select(s => new DisplayLine(s, lineKind)));
                }
                if (message.Role == "tool" && !showDetails) rendered.Add(new(L("▸ /details pour développer", "▸ /details to expand"), "tool"));
                foreach (var image in message.Attachments) rendered.Add(new("▧ " + image.Name, "muted"));
                if (message.Role == "assistant" && message.State != "streaming" && message.Seconds > 0)
                    rendered.Add(new(L("Durée : ", "Duration: ") + $"{message.Seconds:0.#} s", "muted"));
                rendered.Add(new("", "muted"));
                cached = (message, reasoning, width, showDetails, rendered); lineCache[key] = cached;
            }
            lines.AddRange(cached.Lines);
        }
        foreach (var child in view.Children.Values)
        {
            lines.Add(new($"  ↳ {child.Name} · {child.Status}", "heading"));
            lines.AddRange(TerminalText.Wrap("    " + child.Activity + " · " + child.Task, width).Select(s => new DisplayLine(s, "muted")));
        }
        var task = view.Messages.Values.LastOrDefault(m => m.Role == "tasks");
        if (task != null)
        {
            try
            {
                foreach (var todo in JsonNode.Parse(task.Content)!.AsArray())
                    lines.Add(new((S(todo, "status") == "completed" ? "✓ " : S(todo, "status") == "in_progress" ? "◉ " : "○ ") + S(todo, "content"), "task"));
            }
            catch (JsonException) { }
        }
        foreach (var queued in view.Inbox) lines.Add(new(TerminalText.Fit($"#{I(queued, "id")} · {S(queued, "text")}", Math.Max(1, width - 14)), "muted", I(queued, "id")));
        return lines;
    }

    public static string DemoFrame(string? theme = null, int width = 118, int height = 36, bool commands = false, string input = "")
    {
        using var ui = new TerminalUi(new() { Theme = theme });
        ui.workspace = new(new AppState(), [new Project { Id = 1, Name = "OhMyHarness", SourceFolder = "E:/Projects/OhMyHarness" }],
            [new Chat { Id = 1, ProjectId = 1, Title = "Une interface terminal pour mes agents" }, new Chat { Id = 2, ProjectId = 1, Title = "Revue du moteur partagé" }],
            [new Provider { Id = 1, Name = "DeepSeek", Model = "deepseek-flash" }], []);
        ui.chatId = ui.providerId = 1;
        ui.editor.Set(input);
        var view = ui.View(1); view.Tokens = 18520; view.Limit = 128000; view.Speed = 62.8;
        view.Messages[1] = new() { Id = 1, ChatId = 1, Role = "user", Content = "Ajoute une commande pour exporter mes conversations en Markdown." };
        view.Messages[2] = new() { Id = 2, ChatId = 1, Role = "assistant", Content = "Je vais réutiliser le service d’export du moteur partagé.\n\n### Plan\n1. Inspecter le service existant\n2. Brancher /export dans le CLI\n3. Vérifier le fichier Markdown produit" };
        view.Messages[3] = new() { Id = 3, ChatId = 1, Role = "tool", Content = "read_source\nsrc/OhMyHarness.Core/ConversationExport.cs · 180 lignes lues" };
        view.Status = "Démonstration hors ligne · aucune requête API";
        if (commands) ui.dialog = new("Commandes disponibles / Available commands", "", Commands.ToList());
        return string.Join(Environment.NewLine, ui.Draw(width, height).Lines()) + "\x1b[0m\n";
    }
}

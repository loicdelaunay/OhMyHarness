using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    string WorkflowText(string fr, string en) => state.Language == "en" ? en : fr;
    WorkflowTools CreateWorkflow(ConversationRun run) => new(
        async (questions, ct) =>
        {
            var completion = new TaskCompletionSource<AgentAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = WorkflowText("Réponse attendue", "Waiting for your answer"), FontSize = 18 });
            var readers = new List<Func<IReadOnlyList<string>>>();
            foreach (var question in questions)
            {
                panel.Children.Add(new TextBlock { Text = question.Question, TextWrapping = TextWrapping.Wrap });
                var choices = new List<(ToggleSwitch Toggle, string Label)>();
                foreach (var option in question.Options)
                {
                    var toggle = new ToggleSwitch { Header = option.Label, OffContent = option.Description, OnContent = option.Description };
                    toggle.Toggled += (_, _) => { if (toggle.IsOn && !question.Multiple) foreach (var other in choices) if (other.Toggle != toggle) other.Toggle.IsOn = false; };
                    choices.Add((toggle, option.Label)); panel.Children.Add(toggle);
                }
                var free = new TextBox { PlaceholderText = WorkflowText("Votre réponse…", "Your answer…"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000 };
                if (question.Custom) panel.Children.Add(free);
                readers.Add(() => {
                    var values = choices.Where(x => x.Toggle.IsOn).Select(x => x.Label).ToList();
                    if (question.Custom && !string.IsNullOrWhiteSpace(free.Text)) { if (!question.Multiple) values.Clear(); values.Add(free.Text.Trim()); }
                    return values;
                });
            }
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var send = new Button { Content = WorkflowText("Répondre et reprendre", "Answer and resume") };
            var cancel = new Button { Content = WorkflowText("Annuler la question", "Dismiss question") };
            send.Click += (_, _) => {
                var answer = new AgentAnswer(false, readers.Select(x => x()).ToList());
                try { WorkflowTools.ValidateAnswer(questions, answer); completion.TrySetResult(answer); } catch (ArgumentException ex) { error.Text = ex.Message; }
            };
            cancel.Click += (_, _) => completion.TrySetResult(new(true, []));
            buttons.Children.Add(send); buttons.Children.Add(cancel); panel.Children.Add(error); panel.Children.Add(buttons);
            var card = new Border { Child = panel, Padding = new(18), CornerRadius = new(12), BorderThickness = new(1), BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue) };
            run.Messages.Children.Add(card); ScrollRunToBottom(run);
            SetRunStatus(run, WorkflowText("Réponse attendue dans la conversation", "Waiting for your answer in the conversation"));
            try
            {
                var answer = await completion.Task.WaitAsync(ct);
                var summary = string.Join("\n\n", questions.Select((q, i) => q.Question + "\n→ " + (answer.Cancelled ? WorkflowText("Annulé", "Cancelled") : string.Join(", ", answer.Answers[i]))));
                await using var store = new HarnessDb();
                store.Messages.Add(new Message { ChatId = run.Chat.Id, Role = "interaction", State = "ui", Content = summary });
                await store.SaveChangesAsync(ct);
                AddMessage("assistant", summary, [], run.Messages);
                SetRunStatus(run, WorkflowText("Reprise du travail…", "Resuming…"));
                return answer;
            }
            finally { run.Messages.Children.Remove(card); }
        },
        async (items, ct) =>
        {
            await using var store = new HarnessDb();
            var message = await store.Messages.FirstOrDefaultAsync(x => x.ChatId == run.Chat.Id && x.Role == "tasks", ct);
            if (message == null) { message = new() { ChatId = run.Chat.Id, Role = "tasks", State = "ui" }; store.Messages.Add(message); }
            message.Content = items.ToJsonString(); await store.SaveChangesAsync(ct);
            RenderTasks(message.Content, run.Messages);
        });

    void RenderTasks(string json, StackPanel target)
    {
        foreach (var old in target.Children.OfType<FrameworkElement>().Where(x => Equals(x.Tag, "workflow-tasks")).ToList()) target.Children.Remove(old);
        var items = JsonNode.Parse(json) as JsonArray ?? [];
        if (items.Count == 0) return;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = WorkflowText("Tâches", "Tasks") + $" · {items.Count(x => x?["status"]?.GetValue<string>() == "completed")}/{items.Count}", FontSize = 18 });
        foreach (var item in items)
        {
            var status = item?["status"]?.GetValue<string>();
            var label = status switch { "completed" => WorkflowText("✓ Terminée", "✓ Completed"), "in_progress" => WorkflowText("◉ En cours", "◉ In progress"), "cancelled" => WorkflowText("— Annulée", "— Cancelled"), _ => WorkflowText("○ À faire", "○ To do") };
            panel.Children.Add(new TextBlock { Text = label + " · " + item?["content"]?.GetValue<string>(), TextWrapping = TextWrapping.Wrap });
        }
        var card = new Expander { Header = WorkflowText("Étapes du travail", "Work steps"), Content = panel, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "workflow-tasks" };
        target.Children.Insert(0, card);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly Border pinnedTasks = new() { Visibility = Visibility.Collapsed };
    readonly Dictionary<int, bool> taskExpansion = new();
    async Task RefreshPinnedTasksAsync()
    {
        var id = chat?.Id;
        var revision = conversationLoadRevision;
        var json = id == null ? null : await ReadStoreAsync(store => store.Messages.Where(x => x.ChatId == id && x.Role == "tasks").Select(x => x.Content).FirstOrDefault());
        if (chat?.Id == id && revision == conversationLoadRevision) ShowPinnedTasks(json);
    }
    void ShowPinnedTasks(string? json)
    {
        var items = string.IsNullOrWhiteSpace(json) ? new JsonArray() : JsonNode.Parse(json) as JsonArray ?? [];
        if (selectedSubagent != null || chat?.TodoDismissed == true || !items.Any(x => x?["status"]?.GetValue<string>() is "pending" or "in_progress"))
        { pinnedTasks.Child = null; pinnedTasks.Visibility = Visibility.Collapsed; return; }
        var panel = new StackPanel { Spacing = 4 };
        foreach (var item in items)
        {
            var status = item?["status"]?.GetValue<string>();
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var check = new CheckBox { IsChecked = status == "completed", IsHitTestVisible = false, IsTabStop = false, MinWidth = 22, MinHeight = 24 };
            row.Children.Add(check);
            var label = new TextBlock { Text = (status == "in_progress" ? "◉ " : status == "cancelled" ? "— " : "") + item?["content"]?.GetValue<string>(),
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = status == "in_progress" ? FluentDesign.Primary : FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            panel.Children.Add(row);
        }
        var owner = chat!;
        var expanded = taskExpansion.GetValueOrDefault(owner.Id, true);
        var current = items.FirstOrDefault(x => x?["status"]?.GetValue<string>() == "in_progress") ?? items.First(x => x?["status"]?.GetValue<string>() == "pending");
        var index = items.IndexOf(current) + 1;
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var toggle = Action((expanded ? "⌄  " : "›  ") + WorkflowText("Étape", "Step") + $" {index}/{items.Count} · " + current?["content"]?.GetValue<string>(), () => { taskExpansion[owner.Id] = !expanded; ShowPinnedTasks(json); return Task.CompletedTask; });
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch; toggle.HorizontalContentAlignment = HorizontalAlignment.Left;
        header.Children.Add(toggle);
        var close = Action("×", async () => { owner.TodoDismissed = true; await db.SaveChangesAsync(); if (chat?.Id == owner.Id) ShowPinnedTasks(json); });
        ToolTipService.SetToolTip(close, WorkflowText("Masquer la liste · réouvrir depuis +", "Hide list · reopen from +")); Grid.SetColumn(close, 1); header.Children.Add(close);
        var content = new StackPanel { Spacing = 6 }; content.Children.Add(header);
        content.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 140, Visibility = expanded ? Visibility.Visible : Visibility.Collapsed });
        pinnedTasks.Child = FluentDesign.Surface(content, 10);
        pinnedTasks.Visibility = Visibility.Visible;
    }
    string WorkflowText(string fr, string en) => state.Language == "en" ? en : fr;
    WorkflowTools CreateWorkflow(ConversationRun run) => new(
        async (questions, ct) =>
        {
            var completion = new TaskCompletionSource<AgentAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            var panel = new StackPanel { Spacing = 14 };
            var header = new Grid();
            header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var progress = new TextBlock { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
            header.Children.Add(progress);
            var collapse = new Button { Content = "⌄", Padding = new(8, 2, 8, 2), MinWidth = 32,
                Background = FluentDesign.Resource("TransparentBrush"), BorderThickness = new(0) };
            ToolTipService.SetToolTip(collapse, WorkflowText("Réduire ou développer les questions", "Collapse or expand questions"));
            Grid.SetColumn(collapse, 1); header.Children.Add(collapse);
            panel.Children.Add(header);
            var body = new StackPanel { Spacing = 14 };
            panel.Children.Add(body);
            var readers = new List<Func<IReadOnlyList<string>>>();
            var customAnswersValid = new List<Func<bool>>();
            var pages = new List<StackPanel>();
            for (var questionIndex = 0; questionIndex < questions.Count; questionIndex++)
            {
                var question = questions[questionIndex];
                var page = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
                page.Children.Add(new TextBlock { Text = question.Question, TextWrapping = TextWrapping.Wrap,
                    FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary });
                page.Children.Add(new TextBlock { Text = question.Multiple
                        ? WorkflowText("Sélectionnez une ou plusieurs réponses", "Select one or more answers")
                        : WorkflowText("Sélectionnez une réponse", "Select an answer"),
                    FontSize = 13, Foreground = FluentDesign.Secondary });
                var options = new StackPanel { Spacing = 8, Margin = new(0, 8, 0, 0) };
                page.Children.Add(new ScrollViewer { Content = options, MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
                var choices = new List<(ToggleButton Toggle, string Label)>();
                var groupName = "workflow-question-" + Guid.NewGuid().ToString("N");

                Border Choice(string label, string description, ToggleButton toggle)
                {
                    var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                    copy.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap,
                        FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary });
                    if (!string.IsNullOrWhiteSpace(description))
                        copy.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap,
                            FontSize = 14, Foreground = FluentDesign.Secondary });
                    toggle.Content = copy;
                    toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
                    toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    toggle.MinHeight = 62;
                    toggle.Padding = new(12, 11, 12, 11);
                    toggle.Background = FluentDesign.Resource("TransparentBrush");
                    toggle.BorderThickness = new(0);
                    var row = new Border { Child = toggle, Background = FluentDesign.Card,
                        BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(8) };
                    var hovered = false;
                    void Refresh() {
                        row.Background = toggle.IsChecked == true ? FluentDesign.Resource("ControlSelectedBrush")
                            : hovered ? FluentDesign.Resource("ControlHoverBrush") : FluentDesign.Card;
                        row.BorderBrush = toggle.IsChecked == true ? FluentDesign.Resource("AccentFillColorDefaultBrush") : FluentDesign.Stroke;
                    }
                    toggle.Checked += (_, _) => Refresh();
                    toggle.Unchecked += (_, _) => Refresh();
                    row.PointerEntered += (_, _) => { hovered = true; Refresh(); };
                    row.PointerExited += (_, _) => { hovered = false; Refresh(); };
                    return row;
                }
                foreach (var option in question.Options)
                {
                    ToggleButton toggle = question.Multiple ? new CheckBox() : new RadioButton { GroupName = groupName };
                    choices.Add((toggle, option.Label));
                    options.Children.Add(Choice(option.Label, option.Description, toggle));
                }
                var free = new TextBox { PlaceholderText = WorkflowText("Tapez votre réponse…", "Type your answer…"),
                    AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000,
                    MinHeight = 62, MaxHeight = 130, Visibility = Visibility.Collapsed };
                ToggleButton? custom = null;
                if (question.Custom)
                {
                    custom = question.Multiple ? new CheckBox() : new RadioButton { GroupName = groupName };
                    options.Children.Add(Choice(WorkflowText("Tapez votre propre réponse", "Write your own answer"),
                        WorkflowText("Tapez votre réponse…", "Type your answer…"), custom));
                    options.Children.Add(free);
                    custom.Checked += (_, _) => { free.Visibility = Visibility.Visible; free.Focus(FocusState.Programmatic); };
                    custom.Unchecked += (_, _) => free.Visibility = Visibility.Collapsed;
                    if (question.Options.Count == 0) { custom.IsChecked = true; free.Visibility = Visibility.Visible; }
                }
                customAnswersValid.Add(() => custom?.IsChecked != true || !string.IsNullOrWhiteSpace(free.Text));
                readers.Add(() => {
                    var values = choices.Where(x => x.Toggle.IsChecked == true).Select(x => x.Label).ToList();
                    if (custom?.IsChecked == true && !string.IsNullOrWhiteSpace(free.Text)) values.Add(free.Text.Trim());
                    return values;
                });
                body.Children.Add(page); pages.Add(page);
            }
            var currentPage = 0;
            void ShowPage(int index)
            {
                currentPage = index;
                progress.Text = WorkflowText("Question", "Question") + $" {index + 1} / {questions.Count}";
                for (var i = 0; i < pages.Count; i++) pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush") };
            var buttons = new Grid { ColumnSpacing = 8 };
            buttons.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            buttons.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var cancel = new Button { Content = WorkflowText("Annuler", "Cancel") };
            var back = new Button { Content = WorkflowText("Précédente", "Previous") };
            var send = new Button { Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            void UpdateButtons()
            {
                back.Visibility = currentPage == 0 ? Visibility.Collapsed : Visibility.Visible;
                send.Content = currentPage == questions.Count - 1
                    ? WorkflowText("Répondre et reprendre", "Answer and resume") : WorkflowText("Suivante", "Next");
            }
            ShowPage(0); UpdateButtons();
            collapse.Click += (_, _) => { body.Visibility = body.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                collapse.Content = body.Visibility == Visibility.Visible ? "⌄" : "›"; };
            back.Click += (_, _) => { error.Text = ""; ShowPage(currentPage - 1); UpdateButtons(); };
            send.Click += (_, _) => {
                if (!customAnswersValid[currentPage]())
                { error.Text = WorkflowText("Saisissez votre réponse personnalisée.", "Enter your custom answer."); return; }
                try { WorkflowTools.ValidateAnswer([questions[currentPage]], new AgentAnswer(false, [readers[currentPage]()])); }
                catch (ArgumentException) { error.Text = WorkflowText("Sélectionnez une réponse ou saisissez votre texte.", "Select an answer or enter your text."); return; }
                error.Text = "";
                if (currentPage < questions.Count - 1) { ShowPage(currentPage + 1); UpdateButtons(); return; }
                var answer = new AgentAnswer(false, readers.Select(x => x()).ToList());
                try { WorkflowTools.ValidateAnswer(questions, answer); completion.TrySetResult(answer); }
                catch (ArgumentException ex) { error.Text = ex.Message; }
            };
            cancel.Click += (_, _) => completion.TrySetResult(new(true, []));
            buttons.Children.Add(cancel); Grid.SetColumn(back, 1); buttons.Children.Add(back);
            Grid.SetColumn(send, 2); buttons.Children.Add(send);
            body.Children.Add(error); body.Children.Add(buttons);
            var card = new Border { Child = panel, Padding = new(16), CornerRadius = new(16), BorderThickness = new(1),
                Background = FluentDesign.Resource("AssistantMessageFillBrush"), BorderBrush = FluentDesign.Stroke };
            run.Messages.Children.Add(card); ScrollRunToBottom(run);
            SetRunStatus(run, WorkflowText("Réponse attendue dans la conversation", "Waiting for your answer in the conversation"), StatusKind.Notice);
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
            RenderTasks(message.Content, run.Messages, run.Chat.Id);
        });

    void RenderTasks(string json, StackPanel target, int? ownerId = null)
    {
        foreach (var old in target.Children.OfType<FrameworkElement>().Where(x => Equals(x.Tag, "workflow-tasks")).ToList()) target.Children.Remove(old);
        var items = JsonNode.Parse(json) as JsonArray ?? [];
        if ((ownerId ?? chat?.Id) == chat?.Id) ShowPinnedTasks(json);
        if (items.Count == 0) return;
        if (items.Any(x => x?["status"]?.GetValue<string>() is "pending" or "in_progress")) return;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = WorkflowText("Tâches", "Tasks") + $" · {items.Count(x => x?["status"]?.GetValue<string>() == "completed")}/{items.Count}", FontSize = 18 });
        foreach (var item in items)
        {
            var status = item?["status"]?.GetValue<string>();
            var label = status switch { "completed" => WorkflowText("✓ Terminée", "✓ Completed"), "in_progress" => WorkflowText("◉ En cours", "◉ In progress"), "cancelled" => WorkflowText("— Annulée", "— Cancelled"), _ => WorkflowText("○ À faire", "○ To do") };
            panel.Children.Add(new TextBlock { Text = label + " · " + item?["content"]?.GetValue<string>(), TextWrapping = TextWrapping.Wrap });
        }
        var card = new Expander { Header = WorkflowText("Étapes du travail", "Work steps"), Content = panel, IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "workflow-tasks" };
        target.Children.Insert(0, card);
    }
}

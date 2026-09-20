using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    void AttachContextPopover(StackPanel anchor)
    {
        anchor.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var body = new StackPanel { Spacing = 10, Width = 340 };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var compact = new Button { Content = WorkflowText("Compacter maintenant", "Compact now"), HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(details); body.Children.Add(compact);
        var flyout = new Flyout { Content = body };
        bool opened = false;
        var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void Update()
        {
            var limit = ActiveRun?.Provider.ContextLimit ?? provider?.ContextLimit ?? 128000;
            var value = ContextDetails.From(VisibleHistory(), limit);
            var update = ActiveRun?.Update;
            var input = update?.InputTokens ?? value.Input;
            var output = update?.OutputTokens ?? value.Output;
            var used = ActiveRun?.Context?.Tokens ?? (update != null ? (update.InputTokens ?? ActiveRun?.InputEstimate ?? value.Used) + (update.OutputTokens ?? ContextWindow.EstimateText(update.Text + update.Reasoning)) : value.Used);
            var estimated = ActiveRun?.Context?.Estimated ?? (update == null ? value.Estimated : update.InputTokens == null || update.OutputTokens == null);
            details.Text = (estimated ? "≈ " : "") + WorkflowText("Utilisation du contexte", "Context usage") + $"\n{used:N0} / {limit:N0} tokens · {used * 100 / Math.Max(1, limit):F1} %\n" +
                WorkflowText("Disponible : ", "Remaining: ") + $"{Math.Max(0, limit - used):N0}\n" +
                WorkflowText("Dernière entrée déclarée : ", "Last reported input: ") + (input?.ToString("N0") ?? "—") + "\n" +
                WorkflowText("Dernière sortie déclarée : ", "Last reported output: ") + (output?.ToString("N0") ?? "—") + "\n\n" +
                WorkflowText("Historique enregistré — estimations", "Saved history — estimates") + "\n" +
                WorkflowText("Vos messages : ", "Your messages: ") + $"{value.User:N0}\n" +
                WorkflowText("Réponses : ", "Responses: ") + $"{value.Assistant:N0}\n" +
                WorkflowText("Résultats d’outils : ", "Tool results: ") + $"{value.Tools:N0}\n" +
                WorkflowText("Résumé : ", "Summary: ") + $"{value.Summary:N0}\n" +
                WorkflowText("Images (approximation) : ", "Images (approximation): ") + $"{value.Images:N0}\n\n" +
                WorkflowText("Le détail exclut les instructions système et les définitions d’outils ; il peut différer du total. Compactage automatique à 95 %. Le compactage utilise le modèle et peut consommer des tokens.", "The breakdown excludes system instructions and tool definitions; it may differ from the total. Auto-compaction at 95%. Compaction uses the model and may consume tokens.") +
                (ActiveRun == null ? "" : "\n\n" + WorkflowText("Compactage disponible après la réponse.", "Compaction available after the response."));
            compact.IsEnabled = chat != null && provider != null && ActiveRun == null && VisibleHistory().Any(x => x.State == "complete" && x.Role != "compaction");
        }
        dismiss.Tick += (_, _) => { dismiss.Stop(); flyout.Hide(); };
        refresh.Tick += (_, _) => Update();
        anchor.PointerEntered += (_, _) => { dismiss.Stop(); if (!opened) { Update(); flyout.ShowAt(anchor); } };
        anchor.Tapped += (_, _) => { if (!opened) { Update(); flyout.ShowAt(anchor); } };
        anchor.PointerExited += (_, _) => dismiss.Start();
        body.PointerEntered += (_, _) => dismiss.Stop();
        body.PointerExited += (_, _) => dismiss.Start();
        flyout.Opened += (_, _) => { opened = true; refresh.Start(); };
        flyout.Closed += (_, _) => { opened = false; dismiss.Stop(); refresh.Stop(); };
        compact.Click += async (_, _) => { flyout.Hide(); await Guard(CompactCurrentChat); };
    }

    async Task CompactCurrentChat()
    {
        if (chat == null || project == null || provider == null || ActiveRun != null) return;
        using var run = new ConversationRun(chat, project, provider, state, "", []) { Messages = messages };
        conversationRuns.Add(run.Chat.Id, run); RefreshGenerationControls();
        var ct = run.Cancellation.Token;
        SetRunStatus(run, WorkflowText("Compactage manuel du contexte…", "Compacting context…"));
        try
        {
            var secret = KeyVault.Decrypt(run.Provider.ProtectedKey);
            var changed = await ContextDetails.CompactAsync(run, async (text, token) => {
                Completion result;
                if (run.Provider.IsOpenCode)
                {
                    await EnsureOpenCodeServerAsync(run.Provider, secret, token, run.Project);
                    var directory = OpenCodeDirectory(run.Project);
                    var isolated = new Provider { Kind = run.Provider.Kind, BaseUrl = run.Provider.BaseUrl, Username = run.Provider.Username, Model = run.Provider.Model, OpenCodeTools = false };
                    var session = await openCodeEngine.CreateSessionAsync(isolated, secret, directory, "Compactage manuel", token);
                    result = await openCodeEngine.PromptAsync(isolated, secret, directory, session, text, ContextDetails.SummaryInstruction, [], _ => { }, token);
                }
                else result = await engine.StreamAsync(run.Provider, secret, new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = ContextDetails.SummaryInstruction },
                    new JsonObject { ["role"] = "user", ["content"] = text }), [], _ => { }, token);
                return result.Message["content"]?.GetValue<string>() ?? "";
            }, ct);
            if (changed)
            {
                var summary = run.Db.Messages.Local.Last(x => x.Role == "compaction" && x.State == "complete");
                AddMessage("compaction", summary.Content, [], run.Messages);
            }
            SetRunStatus(run, changed ? WorkflowText("Contexte compacté.", "Context compacted.") : WorkflowText("Aucune réduction utile : historique conservé.", "No useful reduction: history preserved."));
        }
        catch (Exception ex) { SetRunStatus(run, ex is OperationCanceledException ? WorkflowText("Compactage arrêté.", "Compaction stopped.") : ex.Message); }
        finally
        {
            try { conversationHistory[run.Chat.Id] = await run.Db.Messages.AsNoTracking().Include(x => x.Attachments).Where(x => x.ChatId == run.Chat.Id).OrderBy(x => x.Id).ToListAsync(); }
            finally
            {
                conversationRuns.Remove(run.Chat.Id); RefreshGenerationControls();
                if (IsVisible(run)) RefreshContextInfo();
            }
        }
    }
}

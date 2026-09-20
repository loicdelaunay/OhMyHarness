using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    // Each generation owns its database context, captured configuration and message view.
    // UI selection is never used to route a response that is already running.
    sealed class ConversationRun(Chat chat, Project project, Provider provider, AppState options,
        string prompt, IEnumerable<Attachment> images) : ConversationSession(chat, project, provider, options, prompt, images)
    {
        public required StackPanel Messages { get; init; }
        public GenerationSpeedTracker? Tracker { get; set; }
        public GenerationUpdate? Update { get; set; }
        public int? InputEstimate { get; set; }
        public (double Tokens, bool Estimated)? Context { get; set; }
        public string Status { get; set; } = "";
        public bool Submitted { get; set; }
    }

    readonly Dictionary<int, ConversationRun> conversationRuns = [];
    readonly Dictionary<int, (string Text, List<Attachment> Images)> conversationDrafts = [];
    readonly Dictionary<int, string> conversationStatuses = [];
    readonly Dictionary<int, List<Message>> conversationHistory = [];
    readonly SemaphoreSlim toolQueue = new(1, 1);
    ConversationRun? ActiveRun => chat != null ? conversationRuns.GetValueOrDefault(chat.Id) : null;
    static StackPanel CreateMessagePanel() => new() { Spacing = 14, Padding = new(4, 20, 12, 20) };
    bool IsVisible(ConversationRun run) => chat?.Id == run.Chat.Id;
    IEnumerable<Message> VisibleHistory() => ActiveRun is { } active ? active.Db.Messages.Local :
        chat != null ? conversationHistory.GetValueOrDefault(chat.Id) ?? [] : [];

    void SaveConversationDraft()
    {
        if (chat != null) conversationDrafts[chat.Id] = (composer.Text, [.. pendingImages]);
    }

    void RestoreConversationDraft()
    {
        pendingImages.Clear(); composer.Text = "";
        if (chat != null && conversationDrafts.TryGetValue(chat.Id, out var draft))
        {
            composer.Text = draft.Text; pendingImages.AddRange(draft.Images);
        }
        UpdateAttachments();
    }

    void RefreshGenerationControls()
    {
        // Keep navigation, settings and drafts usable; only sending to this running chat is blocked.
        send.IsEnabled = chat != null && ActiveRun == null;
        stop.IsEnabled = ActiveRun != null;
        composer.IsEnabled = true;
        RefreshConversationProgress();
    }

    void RefreshConversationProgress()
    {
        static TElement? Find<TElement>(DependencyObject parent) where TElement : DependencyObject
        {
            if (parent is TElement found) return found;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (Find<TElement>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
            return null;
        }
        foreach (var item in chats.Items.OfType<Chat>())
        {
            if (chats.ContainerFromItem(item) is not ListViewItem container) continue;
            if (Find<ProgressBar>(container) is { } bar)
            {
                bar.Visibility = conversationRuns.ContainsKey(item.Id) ? Visibility.Visible : Visibility.Collapsed;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, T("Le modèle réfléchit…") + " " + item.Title);
            }
            if (Find<TextBlock>(container) is { } label) label.Text = item.Title;
        }
    }

    void SetRunStatus(ConversationRun run, string text)
    {
        run.Status = text;
        conversationStatuses[run.Chat.Id] = text;
        if (IsVisible(run)) status.Text = text;
    }

    void ShowContextUsage(ConversationRun run, double tokens, bool estimated = false)
    {
        run.Context = (tokens, estimated);
        if (IsVisible(run)) ShowContextUsage(tokens, estimated, run.Provider.ContextLimit);
    }

    void UpdateMetrics(ConversationRun run, GenerationUpdate update, int? fallbackInputTokens = null)
    {
        run.Update = update; run.InputEstimate = fallbackInputTokens; run.Context = null;
        if (IsVisible(run)) UpdateMetrics(update, fallbackInputTokens, run.Provider.ContextLimit);
    }

    void RestoreRunMetrics(ConversationRun run)
    {
        status.Text = run.Status;
        if (run.Update != null) UpdateMetrics(run.Update, run.InputEstimate, run.Provider.ContextLimit);
        if (run.Context is { } context) ShowContextUsage(context.Tokens, context.Estimated, run.Provider.ContextLimit);
        RefreshSpeedTooltip(run.Tracker);
    }

    void ScrollRunToBottom(ConversationRun run)
    {
        if (IsVisible(run)) ScrollToBottom();
    }

    void MarkRunSubmitted(ConversationRun run)
    {
        run.Submitted = true;
        // Update only the originating list entry, never the newly selected conversation.
        var entry = db.Chats.Local.FirstOrDefault(x => x.Id == run.Chat.Id);
        if (entry != null)
        {
            entry.Title = run.Chat.Title;
            db.Entry(entry).Property(x => x.Title).OriginalValue = run.Chat.Title;
        }
        if (IsVisible(run)) title.Text = run.Chat.Title;
        RefreshConversationProgress();
    }

    async Task SendAsync()
    {
        if (chat == null || provider == null || project == null || ActiveRun != null) return;
        if (string.IsNullOrWhiteSpace(composer.Text) && pendingImages.Count == 0) return;
        var secret = KeyVault.Decrypt(provider.ProtectedKey);
        if ((!provider.IsOpenCode && secret.Length == 0) || string.IsNullOrWhiteSpace(provider.Model))
        { await Settings(); return; }

        var run = new ConversationRun(chat, project, provider, state, composer.Text.Trim(), pendingImages) { Messages = messages };
        conversationRuns.Add(run.Chat.Id, run); // Reserve before the first await, preventing duplicate sends.
        composer.Text = ""; pendingImages.Clear(); UpdateAttachments();
        SaveConversationDraft(); RefreshGenerationControls();
        SetRunStatus(run, T("Le modèle réfléchit…"));
        try
        {
            await run.PrepareSandboxAsync(run.Cancellation.Token);
            if (run.Provider.IsOpenCode) await SendOpenCodeAsync(run, secret);
            else await SendCoreAsync(run, secret);
        }
        catch (Exception ex)
        {
            SetRunStatus(run, ex is OperationCanceledException
                ? T("Génération arrêtée. Réponse partielle conservée.") : T("Erreur : ") + ex.Message);
            if (!run.Submitted)
            {
                if (IsVisible(run)) SaveConversationDraft();
                var draft = conversationDrafts.GetValueOrDefault(run.Chat.Id);
                conversationDrafts[run.Chat.Id] = (run.Prompt + (string.IsNullOrEmpty(draft.Text) ? "" : "\n" + draft.Text),
                    [.. run.Images, .. draft.Images ?? []]);
                if (IsVisible(run)) RestoreConversationDraft();
            }
        }
        finally
        {
            try { await run.Db.SaveChangesAsync(); }
            catch (Exception ex) { SetRunStatus(run, T("Erreur : ") + ex.Message); }
            if (run.Submitted) conversationHistory[run.Chat.Id] = run.Db.Messages.Local.ToList();
            conversationRuns.Remove(run.Chat.Id);
            run.Dispose();
            RefreshGenerationControls();
            if (IsVisible(run)) RestoreRunMetrics(run);
        }
    }

    async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, CancellationToken ct = default)
    {
        await approvalQueue.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            using var registration = ct.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
            return await dialog.ShowAsync();
        }
        finally { approvalQueue.Release(); }
    }
}

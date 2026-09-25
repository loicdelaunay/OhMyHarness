using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OhMyHarness.Core;
using System.Globalization;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    void ApplyChatSearch(int? selectedId, bool selectFirst = false, bool scrollToFirst = false)
    {
        var query = chatSearch.Text.Trim();
        var matches = query.Length == 0
            ? allProjectChats
            : allProjectChats.Where(item => CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                item.Title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToList();

        var wasLoading = loading;
        loading = true;
        try
        {
            visibleProjectChats.Clear();
            visibleArchivedChats.Clear();
            foreach (var item in matches.Where(c => !c.IsArchived).OrderByDescending(c => c.IsFavorite)) visibleProjectChats.Add(item);
            foreach (var item in matches.Where(c => c.IsArchived)) visibleArchivedChats.Add(item);
            var activeMatch = visibleProjectChats.FirstOrDefault(item => item.Id == selectedId);
            var archivedMatch = visibleArchivedChats.FirstOrDefault(item => item.Id == selectedId);
            chats.SelectedItem = activeMatch ?? (archivedMatch == null && selectFirst ? visibleProjectChats.FirstOrDefault() : null);
            archivedChats.SelectedItem = archivedMatch ?? (chats.SelectedItem == null && selectFirst ? visibleArchivedChats.FirstOrDefault() : null);
            if (archivedChats.SelectedItem != null) archiveExpander.IsExpanded = true;
        }
        finally { loading = wasLoading; }

        UpdateChatSearchSummary();
        RefreshConversationProgress();
        if (scrollToFirst && visibleProjectChats.Count > 0)
        {
            var first = visibleProjectChats[0];
            DispatcherQueue.TryEnqueue(() =>
            {
                if (chats.Items.Count > 0 && ReferenceEquals(chats.Items[0], first)) chats.ScrollIntoView(first);
            });
        }
    }

    void UpdateChatSearchSummary()
    {
        var total = allProjectChats.Count;
        var shown = chats.Items.Count + archivedChats.Items.Count;
        var selected = chats.SelectedItems.Count + archivedChats.SelectedItems.Count;
        chatCount.Text = selected > 1
            ? WorkflowText($"{selected} sélectionnées", $"{selected} selected")
            : chatSearch.Text.Trim().Length == 0 ? total.ToString() : $"{shown} / {total}";
        archiveHeading.Text = WorkflowText($"Archive ({allProjectChats.Count(c => c.IsArchived)})", $"Archive ({allProjectChats.Count(c => c.IsArchived)})");
        chatEmpty.Text = UiText.T(total == 0 ? "Aucune conversation" : "Aucun résultat");
        chatEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task ConversationSelectionChangedAsync(ListView source)
    {
        if (loading) return;
        if (source.SelectedItems.Count == 0) { UpdateChatSearchSummary(); RefreshConversationProgress(); return; }
        var other = ReferenceEquals(source, chats) ? archivedChats : chats;
        var wasLoading = loading;
        loading = true;
        try { other.SelectedItems.Clear(); }
        finally { loading = wasLoading; }
        UpdateChatSearchSummary();
        RefreshConversationProgress();
        // Extended selection handles Shift+click ranges and Ctrl+click. A range is
        // for bulk actions; it must not repeatedly reload the conversation.
        if (source.SelectedItems.Count == 1) await SelectChat();
    }

    void OpenConversationMenu(ListView source, RightTappedRoutedEventArgs e)
    {
        var target = GetChatFromOriginalSource(e.OriginalSource);
        if (target == null) return;
        if (!source.SelectedItems.OfType<Chat>().Any(c => c.Id == target.Id)) source.SelectedItem = target;
        var selected = source.SelectedItems.OfType<Chat>().ToArray();
        if (selected.Length == 0) selected = [target];
        e.Handled = true;

        var menu = new MenuFlyout();
        if (selected.Length == 1)
        {
            var rename = new MenuFlyoutItem { Text = UiText.T("Renommer") };
            rename.Click += async (_, _) => await Guard(() => RenameChatAsync(target));
            menu.Items.Add(rename);
            var autoName = new MenuFlyoutItem { Text = WorkflowText("Nommer avec l’IA", "Name with AI"), Icon = new FontIcon { Glyph = "\uE8D4" } };
            autoName.Click += async (_, _) => await Guard(() => AutoNameAsync(target.Id));
            menu.Items.Add(autoName);
            var favorite = new MenuFlyoutItem { Text = WorkflowText(target.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris", target.IsFavorite ? "Remove favorite" : "Add favorite"), Icon = new FontIcon { Glyph = "\uE734" } };
            favorite.Click += async (_, _) => await Guard(() => ToggleFavoriteAsync(target));
            menu.Items.Add(favorite);
        }
        var archive = new MenuFlyoutItem
        {
            Text = source == archivedChats
                ? WorkflowText(selected.Length == 1 ? "Restaurer" : $"Restaurer {selected.Length} conversations", selected.Length == 1 ? "Restore" : $"Restore {selected.Length} conversations")
                : WorkflowText(selected.Length == 1 ? "Archiver" : $"Archiver {selected.Length} conversations", selected.Length == 1 ? "Archive" : $"Archive {selected.Length} conversations"),
            Icon = new FontIcon { Glyph = source == archivedChats ? "\uE7BA" : "\uE7B8" }
        };
        archive.Click += async (_, _) => await Guard(() => ArchiveChatsAsync(selected, source != archivedChats));
        menu.Items.Add(archive);
        var delete = new MenuFlyoutItem
        {
            Text = WorkflowText(selected.Length == 1 ? "Supprimer…" : $"Supprimer {selected.Length} conversations…", selected.Length == 1 ? "Delete…" : $"Delete {selected.Length} conversations…"),
            Icon = new FontIcon { Glyph = "\uE74D" },
            IsEnabled = selected.All(c => !conversationRuns.ContainsKey(c.Id))
        };
        delete.Click += async (_, _) => await Guard(() => DeleteChatsAsync(selected));
        menu.Items.Add(delete);
        menu.ShowAt(source, e.GetPosition(source));
    }
}

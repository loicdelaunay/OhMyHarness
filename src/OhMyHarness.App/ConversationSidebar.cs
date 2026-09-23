using Microsoft.UI.Xaml;
using OhMyHarness.Core;
using System.Globalization;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    void ApplyChatSearch(int? selectedId, bool selectFirst = false, bool scrollToFirst = false)
    {
        var query = chatSearch.Text.Trim();
        var visible = query.Length == 0
            ? allProjectChats
            : allProjectChats.Where(item => CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                item.Title, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToList();

        var wasLoading = loading;
        loading = true;
        try
        {
            visibleProjectChats.Clear();
            foreach (var item in visible) visibleProjectChats.Add(item);
            chats.SelectedItem = visibleProjectChats.FirstOrDefault(item => item.Id == selectedId)
                ?? (selectFirst ? visibleProjectChats.FirstOrDefault() : null);
        }
        finally { loading = wasLoading; }

        UpdateChatSearchSummary();
        RefreshConversationProgress();
        if (scrollToFirst && visible.Count > 0)
        {
            var first = visible[0];
            DispatcherQueue.TryEnqueue(() =>
            {
                if (chats.Items.Count > 0 && ReferenceEquals(chats.Items[0], first)) chats.ScrollIntoView(first);
            });
        }
    }

    void UpdateChatSearchSummary()
    {
        var total = allProjectChats.Count;
        var shown = chats.Items.Count;
        chatCount.Text = chatSearch.Text.Trim().Length == 0 ? total.ToString() : $"{shown} / {total}";
        chatEmpty.Text = UiText.T(total == 0 ? "Aucune conversation" : "Aucun résultat");
        chatEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

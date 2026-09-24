using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async void FavoriteClick(object sender, RoutedEventArgs e)
    {
        if (GetChatFromOriginalSource(sender) is { } item) await Guard(() => ToggleFavoriteAsync(item));
    }
    async Task ToggleFavoriteAsync(Chat item)
    {
        var value = !item.IsFavorite;
        await using var store = new HarnessDb();
        await store.Chats.Where(c => c.Id == item.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.IsFavorite, value));
        item.IsFavorite = value;
        if (db.Entry(item).State != EntityState.Detached) db.Entry(item).Property(c => c.IsFavorite).OriginalValue = value;
        ApplyChatSearch(chat?.Id);
    }
    async Task AutoNameAsync(int id, bool automatic = false)
    {
        try
        {
            var name = await ConversationNaming.RenameAsync(HarnessDb.DatabasePath, id, http, (key, _) => Task.FromResult(KeyVault.Decrypt(key)), automatic, CancellationToken.None);
            if (name == null) return;
            var item = allProjectChats.FirstOrDefault(c => c.Id == id);
            if (item != null) { item.Title = name; if (db.Entry(item).State != EntityState.Detached) db.Entry(item).Property(c => c.Title).OriginalValue = name; }
            if (conversationRuns.TryGetValue(id, out var run)) { run.Chat.Title = name; run.Db.Entry(run.Chat).Property(c => c.Title).OriginalValue = name; }
            if (chat?.Id == id) { chat.Title = name; title.Text = name; }
            ApplyChatSearch(chat?.Id);
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, "conversation.naming_failed", ex, id);
            if (!automatic) throw;
        }
    }
}

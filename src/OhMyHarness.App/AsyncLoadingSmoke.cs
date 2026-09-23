using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeConversationLoadingAsync(Chat original, Chat other, string output)
    {
        Task Select(Chat target)
        {
            loading = true;
            try { chats.SelectedItem = target; }
            finally { loading = false; }
            return SelectChat();
        }
        var fixtures = Enumerable.Range(0, 73).Select(i => new Message
        {
            ChatId = other.Id, Role = "user", Content = $"Async history fixture {i:D3}"
        }).ToList();
        // The oldest screenshot should not be read until its page is requested.
        fixtures[0].Attachments.Add(new Attachment { Name = "pixel.png", Mime = "image/png", Data = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=") });
        db.Messages.AddRange(fixtures); await db.SaveChangesAsync();
        composer.Text = "draft kept during async switching";
        var pending = Select(other);
        if (!conversationLoading || send.IsEnabled || scroll.Content is not StackPanel skeleton || skeleton.Children.Count == 0)
            throw new Exception("Conversation navigation did not immediately show a local loading state.");
        var ticks = 0;
        var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        heartbeat.Tick += (_, _) => ticks++;
        heartbeat.Start();
        try { await pending; }
        finally { heartbeat.Stop(); }
        if (ticks == 0 || conversationLoading || !conversationReady) throw new Exception("UI did not process events during conversation loading.");
        if (conversationHistory[other.Id][0].Attachments[0].Data.Length != 0)
            throw new Exception("An offscreen image was eagerly loaded.");
        if (messages.Children.OfType<Border>().Count() != HistoryPageSize)
            throw new Exception("Initial history rendering was not limited to the recent page.");
        var seen = 0;
        await Task.Delay(100);
        SetChatFollow(false);
        while (historyPagination.TryGetValue(messages, out var page))
        {
            var before = messages.Children.OfType<Border>().Count();
            scroll.UpdateLayout();
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(30);
            for (var attempt = 0; attempt < 100 && (page.Loading || messages.Children.OfType<Border>().Count() == before); attempt++) await Task.Delay(20);
            if (page.Loading || messages.Children.OfType<Border>().Count() <= before || ++seen > 4)
                throw new Exception("Scrolling to the top did not load older history.");
            if (scroll.VerticalOffset <= 64 || followChatTail)
                throw new Exception("History prepend lost the reading position or resumed auto-scroll.");
        }
        if (messages.Children.OfType<Border>().Count() != fixtures.Count || conversationHistory[other.Id][0].Attachments[0].Data.Length == 0)
            throw new Exception("History pagination lost messages or attachments.");
        var first = Select(original);
        var second = Select(other);
        var third = Select(original);
        await Task.WhenAll(first, second, third);
        if (chat?.Id != original.Id || title.Text != original.Title || composer.Text != "draft kept during async switching" || conversationLoading)
            throw new Exception("Rapid A/B/A navigation displayed a stale conversation or lost its draft.");
        var changingChat = Select(other);
        var changingProject = SelectProject();
        await Task.WhenAll(changingChat, changingProject);
        if (conversationLoading || !conversationReady) throw new Exception("Project selection did not supersede an unfinished chat load.");
        await Select(original);

        using var run = new ConversationRun(other, project!, provider!, state, "", [], db.Providers.Local) { Messages = CreateMessagePanel() };
        run.Messages.Children.Add(Label("Active run must survive navigation"));
        conversationRuns.Add(other.Id, run);
        try
        {
            await Select(other);
            await Select(original);
            await Select(other);
            if (!ReferenceEquals(scroll.Content, run.Messages) || run.Cancellation.IsCancellationRequested)
                throw new Exception("Navigation replaced or cancelled a running conversation.");
        }
        finally { conversationRuns.Remove(other.Id); }
        await Select(original);
        await Capture(root, Path.Combine(output, "async-conversation-navigation.png"));
    }
}

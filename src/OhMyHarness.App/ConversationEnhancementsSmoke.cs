using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeConversationEnhancements(string output)
    {
        var fixture = new Project { Name = "Conversation UX", Chats = [new Chat { Title = "Lecture stable pendant le streaming" }, new Chat { Title = "Conversation favorite" }] };
        db.Projects.Add(fixture); await db.SaveChangesAsync();
        projects.ItemsSource = new[] { fixture }; projects.SelectedItem = fixture; await SelectProject();
        var favorite = allProjectChats.Single(c => c.Title == "Conversation favorite");
        await ToggleFavoriteAsync(favorite);
        if (chats.Items[0] is not Chat { IsFavorite: true }) throw new Exception("Favorite was not pinned first.");
        await Task.Delay(150); RefreshConversationProgress();
        if (chats.ContainerFromItem(favorite) is not ListViewItem container || FindConversationElement<Button>(container, "conversation-favorite") is not { Opacity: 1 }) throw new Exception("Favorite star unavailable.");
        if (chats.SelectionMode != ListViewSelectionMode.Extended || archivedChats.SelectionMode != ListViewSelectionMode.Extended)
            throw new Exception("Shift range selection is unavailable.");
        chats.SelectedItem = favorite;
        await Task.Delay(100);
        var openChat = chat?.Id;
        var second = allProjectChats.Single(c => c.Id != favorite.Id);
        chats.SelectedItems.Add(second);
        await Task.Delay(100);
        if (chats.SelectedItems.Count != 2 || chat?.Id != openChat || !chatCount.Text.Contains("2"))
            throw new Exception("Multi-selection reloaded the conversation or lost its selection count.");
        await ArchiveChatsAsync(chats.SelectedItems.OfType<Chat>().ToArray(), true);
        if (chats.Items.Count != 0 || archivedChats.Items.Count != 2 || !archiveExpander.IsExpanded)
            throw new Exception("Archived conversations were not moved to the project subgroup.");
        await Task.Delay(100); await Capture(root, Path.Combine(output, "conversation-archive.png"));
        await SelectProject();
        if (archivedChats.Items.Count != 2 || chat?.Id != openChat || !allProjectChats.All(c => c.IsArchived))
            throw new Exception("Archive state or current conversation was lost after a project reload.");
        await ArchiveChatsAsync(visibleArchivedChats.ToArray(), false);
        if (chats.Items.Count != 2 || archivedChats.Items.Count != 0)
            throw new Exception("Restored conversations did not return to the active list.");
        var target = chat!;
        var pending = new PendingInput { ChatId = target.Id, ProviderId = state.ProviderId, Text = "Corrige les problèmes suivants dans l’UI et CLI : vérifier les tests puis résumer les modifications.\nConserver les fonctionnalités existantes." };
        db.PendingInputs.Add(pending); await db.SaveChangesAsync(); await RefreshInboxAsync();
        var queueCard = inboxPanel.Children.OfType<Border>().Single();
        if (queueCard.Child is not Grid grid || grid.ColumnDefinitions.Count != 3 || grid.Children.OfType<StackPanel>().Single().Children.OfType<Button>().Count() != 3) throw new Exception("Queue actions are not three inline buttons.");
        AddMessage("user", "Je lis le début de la réponse pendant que l’agent continue.", [], messages);
        var assistant = AddAssistantMessage("## Résultat\n\nPremier paragraphe terminé.\n\nRéponse en cours", target: messages);
        var first = assistant.BodyContainer.Children[0];
        var oldLast = assistant.BodyContainer.Children[^1];
        SetChatFollow(false);
        assistant.UpdateContent("## Résultat\n\nPremier paragraphe terminé.\n\nRéponse en cours, nouveau texte", streaming: true);
        if (!ReferenceEquals(first, assistant.BodyContainer.Children[0]) || !ReferenceEquals(oldLast, assistant.BodyContainer.Children[^1])) throw new Exception("Streaming repainted paused reading content.");
        SetChatFollow(true);
        if (!ReferenceEquals(first, assistant.BodyContainer.Children[0]) || ReferenceEquals(oldLast, assistant.BodyContainer.Children[^1])) throw new Exception("Resuming did not preserve stable blocks and flush the latest text.");
        assistant.SetDuration(73.2);
        if (!assistant.Duration.Text.Contains("1 min")) throw new Exception("Duration missing from response footer.");
        if (assistant.Duration.Visibility != Visibility.Visible || assistant.Duration.Opacity != 0) throw new Exception("Response duration should reserve its space but remain hidden until hover.");
        var assistantBubble = assistant.Container ?? throw new Exception("Assistant bubble missing.");
        assistantBubble.UpdateLayout();
        var heightBeforeHover = assistantBubble.ActualHeight;
        assistant.SetHovered(true);
        assistantBubble.UpdateLayout();
        if (assistant.Duration.Opacity != 1) throw new Exception("Response duration did not appear on hover.");
        if (Math.Abs(assistantBubble.ActualHeight - heightBeforeHover) > 0.5) throw new Exception("Hover changed the assistant bubble height.");
        assistant.SetHovered(false);
        assistantBubble.UpdateLayout();
        if (assistant.Duration.Opacity != 0 || Math.Abs(assistantBubble.ActualHeight - heightBeforeHover) > 0.5) throw new Exception("Response duration remained visible or resized the bubble after hover.");
        var preferences = BuildConversationPreferences(); var settings = FeatureSettings.Read(state.FeaturesJson); preferences.Save(settings);
        if (!preferences.Validate() || settings.LogRetentionDays != 7) throw new Exception("Conversation preferences defaults invalid.");
        await Task.Delay(200); await Capture(root, Path.Combine(output, "conversation-enhancements.png"));
        var overlay = new Border { Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Padding = new(24), Width = 650, HorizontalAlignment = HorizontalAlignment.Center, Child = new ScrollViewer { Content = preferences.Panel, MaxHeight = 650 } };
        root.Children.Add(overlay); await Task.Delay(100); await Capture(root, Path.Combine(output, "conversation-preferences.png")); root.Children.Remove(overlay);
        File.WriteAllText(Path.Combine(output, "smoke-ok.txt"), "Favorites, multi-selection, persistent archive/restore, inline queue actions, stable streaming, duration footer and settings defaults passed.");
    }
}

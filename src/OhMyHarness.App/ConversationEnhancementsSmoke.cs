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
        if (assistant.Duration.Visibility != Visibility.Collapsed) throw new Exception("Response duration should be hidden until hover.");
        assistant.SetHovered(true);
        if (assistant.Duration.Visibility != Visibility.Visible) throw new Exception("Response duration did not appear on hover.");
        assistant.SetHovered(false);
        if (assistant.Duration.Visibility != Visibility.Collapsed) throw new Exception("Response duration remained visible after hover.");
        var preferences = BuildConversationPreferences(); var settings = FeatureSettings.Read(state.FeaturesJson); preferences.Save(settings);
        if (!preferences.Validate() || settings.LogRetentionDays != 7) throw new Exception("Conversation preferences defaults invalid.");
        await Task.Delay(200); await Capture(root, Path.Combine(output, "conversation-enhancements.png"));
        var overlay = new Border { Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Padding = new(24), Width = 650, HorizontalAlignment = HorizontalAlignment.Center, Child = new ScrollViewer { Content = preferences.Panel, MaxHeight = 650 } };
        root.Children.Add(overlay); await Task.Delay(100); await Capture(root, Path.Combine(output, "conversation-preferences.png")); root.Children.Remove(overlay);
        File.WriteAllText(Path.Combine(output, "smoke-ok.txt"), "Favorites persistence/order/star, inline queue icon buttons, paused streaming/reconciliation, duration footer and settings defaults passed.");
    }
}

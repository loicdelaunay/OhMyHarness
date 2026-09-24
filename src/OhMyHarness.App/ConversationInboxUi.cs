using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly StackPanel inboxPanel=new(){Spacing=4};
    UIElement BuildInbox()
    {
        var panel=new StackPanel{Spacing=4};panel.Children.Add(new ScrollViewer{Content=inboxPanel,MaxHeight=160,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        return panel;
    }
    async Task RefreshInboxAsync()
    {
        var id=chat?.Id; var revision = conversationLoadRevision;
        var pending=id==null?new List<PendingInput>():await ReadStoreAsync(context => context.PendingInputs.AsNoTracking().Where(x=>x.ChatId==id).OrderBy(x=>x.Id).ToList());
        if(chat?.Id!=id || revision != conversationLoadRevision)return;
        inboxPanel.Children.Clear();
        foreach(var item in pending)
        {
            var preview = item.Text[..Math.Min(500, item.Text.Length)].Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            var text = Label(preview + (item.Text.Length > 500 ? "…" : "") + (item.Images().Count > 0 ? $"  · {item.Images().Count} image(s)" : ""), 14);
            text.TextWrapping = TextWrapping.NoWrap;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.MaxLines = 1;
            ToolTipService.SetToolTip(text, item.Text);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var queueIcon = new TextBlock { Text = "↳", FontSize = 16, Foreground = FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(queueIcon, WorkflowText(item.Mode == "steering" ? "À la prochaine étape" : "Message en attente", item.Mode == "steering" ? "At the next step" : "Queued message"));
            row.Children.Add(queueIcon);
            text.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var delete = Action(WorkflowText("Supprimer", "Delete"), async()=>{await using var db=new HarnessDb();await db.PendingInputs.Where(x=>x.Id==item.Id && x.ChatId==item.ChatId).ExecuteDeleteAsync();await RefreshInboxAsync();});
            async Task EditAsync()
            {
                var editor = new TextBox { Text = item.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 300 };
                var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = WorkflowText("Modifier le message en attente", "Edit queued message"), Content = editor,
                    PrimaryButtonText = WorkflowText("Enregistrer", "Save"), CloseButtonText = WorkflowText("Annuler", "Cancel") };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await ConversationInbox.UpdateAsync(HarnessDb.DatabasePath, item.ChatId, item.Id, item.Text, editor.Text);
                    await RefreshInboxAsync();
                }
            }
            var steer = Action("Steer", async()=>
            {
                if (!conversationRuns.TryGetValue(item.ChatId, out var active)) throw new InvalidOperationException(WorkflowText("Aucune exécution en cours.", "No active run."));
                await ConversationInbox.UpdateAsync(HarnessDb.DatabasePath, item.ChatId, item.Id, item.Text, item.Text, true, active.SelectedProviderId);
                await RefreshInboxAsync();
            });
            steer.IsEnabled = item.Mode == "queued" && conversationRuns.ContainsKey(item.ChatId);
            FluentDesign.IconButton(steer, "\uE72A", WorkflowText("Orienter", "Steer"));
            ToolTipService.SetToolTip(steer, WorkflowText("Transmettre à l’agent à sa prochaine étape", "Send to the agent at its next step"));
            actions.Children.Add(steer);
            FluentDesign.IconButton(delete, "\uE74D", WorkflowText("Supprimer", "Delete"), false);
            actions.Children.Add(delete);
            var menu = new MenuFlyout();
            var edit = new MenuFlyoutItem { Text = WorkflowText("Modifier", "Edit"), Icon = new SymbolIcon(Symbol.Edit) };
            edit.Click += async (_, _) => await Guard(EditAsync);
            menu.Items.Add(edit);
            var more = new Button { Flyout = menu };
            FluentDesign.IconButton(more, "\uE712", WorkflowText("Plus d’actions", "More actions"), false);
            actions.Children.Add(more);
            foreach (var button in actions.Children.OfType<Button>())
            {
                button.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                button.Resources["ButtonBackgroundDisabled"] = button.Background;
                button.BorderThickness = new(0);
                button.Foreground = FluentDesign.Secondary;
                button.Padding = new(7, 5, 7, 5);
                button.MinWidth = 30;
                button.MinHeight = 30;
                button.CornerRadius = new(6);
            }
            actions.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            var card = FluentDesign.Surface(row, 0);
            card.Padding = new(12, 4, 6, 4);
            card.CornerRadius = new(16);
            inboxPanel.Children.Add(card);
        }
        if(pending.Count>0 && ActiveRun==null)inboxPanel.Children.Add(Action("Reprendre la file / Resume queue",()=>RunNextQueuedAsync(id!.Value,messages)));
    }
    async Task RunNextQueuedAsync(int chatId,StackPanel panel)
    {
        while (conversationLoading && chat?.Id == chatId) await Task.Delay(20);
        if(conversationRuns.ContainsKey(chatId))return;
        await using var context=new HarnessDb();
        var item=await context.PendingInputs.Where(x=>x.ChatId==chatId).OrderByDescending(x=>x.Mode=="steering").ThenBy(x=>x.Id).FirstOrDefaultAsync();
        if(item==null)return;
        var target=await context.Chats.SingleAsync(x=>x.Id==chatId);
        var project=await context.Projects.SingleAsync(x=>x.Id==target.ProjectId);
        var all=await context.Providers.ToListAsync();var provider=all.Single(x=>x.Id==item.ProviderId);
        var options=await context.States.SingleAsync();
        var run=new ConversationRun(target,project,provider,options,item.Text,item.Images(),all){Messages=this.chat?.Id==chatId&&selectedSubagent==null?messages:panel,PendingInputId=item.Id};
        if(conversationRuns.ContainsKey(chatId)){run.Dispose();return;}
        conversationRuns.Add(chatId,run);await ExecuteRunAsync(run);
    }
    async Task<List<Message>> ApplySteeringAsync(ConversationRun run,CancellationToken ct)
    {
        var added=await ConversationInbox.ApplySteeringAsync(run,ct);
        foreach(var message in added)AddMessage("user",message.Content,message.Attachments,run.Messages);
        if(added.Count>0){await RefreshInboxAsync();ScrollRunToBottom(run);}
        return added;
    }
}

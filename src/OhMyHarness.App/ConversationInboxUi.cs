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
        var id=chat?.Id;await using var context=new HarnessDb();
        var pending=id==null?new List<PendingInput>():await context.PendingInputs.Where(x=>x.ChatId==id).OrderBy(x=>x.Id).ToListAsync();
        if(chat?.Id!=id)return;
        inboxPanel.Children.Clear();
        if (pending.Count > 0) inboxPanel.Children.Add(new TextBlock { Text = WorkflowText("Messages en attente", "Queued messages") + $" · {pending.Count}", FontSize = 12, Foreground = FluentDesign.Secondary, Margin = new(12, 4, 12, 4) });
        foreach(var item in pending)
        {
            var text=Label((item.Mode=="steering"?"↳ ":$"{pending.IndexOf(item)+1}. ")+item.Text[..Math.Min(220,item.Text.Length)] + (item.Text.Length > 220 ? "…" : "") + (item.Images().Count > 0 ? $"  · {item.Images().Count} image(s)" : ""),12);
            text.TextWrapping = TextWrapping.Wrap;
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            actions.Children.Add(Action(WorkflowText("Supprimer", "Delete"), async()=>{await using var db=new HarnessDb();await db.PendingInputs.Where(x=>x.Id==item.Id && x.ChatId==item.ChatId).ExecuteDeleteAsync();await RefreshInboxAsync();}));
            actions.Children.Add(Action(WorkflowText("Modifier", "Edit"), async()=>
            {
                var editor = new TextBox { Text = item.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 300 };
                var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = WorkflowText("Modifier le message en attente", "Edit queued message"), Content = editor,
                    PrimaryButtonText = WorkflowText("Enregistrer", "Save"), CloseButtonText = WorkflowText("Annuler", "Cancel") };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await ConversationInbox.UpdateAsync(HarnessDb.DatabasePath, item.ChatId, item.Id, item.Text, editor.Text);
                    await RefreshInboxAsync();
                }
            }));
            var steer = Action("Steer", async()=>
            {
                if (!conversationRuns.TryGetValue(item.ChatId, out var active)) throw new InvalidOperationException(WorkflowText("Aucune exécution en cours.", "No active run."));
                await ConversationInbox.UpdateAsync(HarnessDb.DatabasePath, item.ChatId, item.Id, item.Text, item.Text, true, active.SelectedProviderId);
                await RefreshInboxAsync();
            });
            steer.IsEnabled = item.Mode == "queued" && conversationRuns.ContainsKey(item.ChatId);
            ToolTipService.SetToolTip(steer, WorkflowText("Transmettre à l’agent à sa prochaine étape", "Send to the agent at its next step"));
            actions.Children.Add(steer); row.Children.Add(actions); inboxPanel.Children.Add(FluentDesign.Surface(row, 10));
        }
        if(pending.Count>0 && ActiveRun==null)inboxPanel.Children.Add(Action("Reprendre la file / Resume queue",()=>RunNextQueuedAsync(id!.Value,messages)));
    }
    async Task RunNextQueuedAsync(int chatId,StackPanel panel)
    {
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

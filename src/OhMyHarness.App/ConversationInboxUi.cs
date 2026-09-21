using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly ComboBox deliveryMode=new(){ItemsSource=new[]{"File d’attente / Queue","Dans l’exécution en cours / Steer"},SelectedIndex=0,MinWidth=180};
    readonly StackPanel inboxPanel=new(){Spacing=4};
    UIElement BuildInbox()
    {
        var panel=new StackPanel{Spacing=4};panel.Children.Add(deliveryMode);panel.Children.Add(new ScrollViewer{Content=inboxPanel,MaxHeight=140,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        ToolTipService.SetToolTip(deliveryMode,"Pendant une génération : nouveau tour après la fin, ou consigne à la prochaine étape. La requête déjà en cours n’est pas modifiée.");
        return panel;
    }
    async Task RefreshInboxAsync()
    {
        var id=chat?.Id;await using var context=new HarnessDb();
        var pending=id==null?new List<PendingInput>():await context.PendingInputs.Where(x=>x.ChatId==id).OrderBy(x=>x.Id).ToListAsync();
        if(chat?.Id!=id)return;
        inboxPanel.Children.Clear();
        foreach(var item in pending)
        {
            var text=Label((item.Mode=="steering"?"↳ Prochaine étape : ":"⏳ En attente : ")+item.Text[..Math.Min(120,item.Text.Length)],12);
            var remove=Action("×",async()=>{await using var db=new HarnessDb();await db.PendingInputs.Where(x=>x.Id==item.Id).ExecuteDeleteAsync();await RefreshInboxAsync();});
            inboxPanel.Children.Add(Row(text,remove));
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

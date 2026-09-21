using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using System.Text.Json.Nodes;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly Dictionary<string,SubagentRecord> subagentViews=[];
    string? selectedSubagent;
    StackPanel? childPanel;
    void UpdateSubagent(ConversationRun run, SubagentRecord child)
    {
        subagentViews[child.Id]=child;
        if(!run.Messages.Children.OfType<Button>().Any(x=>Equals(x.Tag,child.Id)))run.Messages.Children.Add(ChildBubble(child));
        foreach(var button in run.Messages.Children.OfType<Button>().Where(x=>Equals(x.Tag,child.Id)))button.Content=ChildLabel(child);
        RefreshSubagentSidebar();
        if(selectedSubagent==child.Id)RenderSubagent(child);
        else if(IsVisible(run))ScrollToBottom();
    }
    string ChildLabel(SubagentRecord child)=>$"↳ {child.Name} · {child.Status}\n{child.Activity}\n{child.Task[..Math.Min(180,child.Task.Length)]}";
    Button ChildBubble(SubagentRecord child)
    {
        var button=new Button {Tag=child.Id,Content=ChildLabel(child),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(16)};
        button.Click+=(_,e)=> {OpenSubagent(child.Id);};return button;
    }
    void OpenSubagent(string id)
    {
        if(!subagentViews.TryGetValue(id,out var child))return;
        selectedSubagent=id;childPanel=CreateMessagePanel();scroll.Content=childPanel;followChatTail=true;
        RenderSubagent(child);RefreshGenerationControls();
    }
    void RenderSubagent(SubagentRecord child)
    {
        if(childPanel==null)return;
        childPanel.Children.Clear();
        childPanel.Children.Add(Action("← Conversation parente / Parent conversation", async()=>{selectedSubagent=null;childPanel=null;await SelectChat();}));
        childPanel.Children.Add(Label(child.Name+" · "+child.Status,22));childPanel.Children.Add(Label(child.Activity,13));
        var content=new StackPanel {Spacing=12};childPanel.Children.Add(content);
        var transcript="## Tâche / Task\n"+child.Task+"\n\n";
        foreach(var message in JsonNode.Parse(child.TranscriptJson)?.AsArray() ?? [])
        {
            transcript+="\n### "+(message?["role"]?.GetValue<string>()??"")+"\n"+(message?["content"]?.GetValue<string>()??"")+"\n";
            if(message?["tool_calls"]!=null)transcript+="\n```json\n"+message["tool_calls"]!.ToJsonString()+"\n```\n";
        }
        MarkdownRenderer.RenderTo(content,transcript);
        ScrollToBottom();
    }
    async Task LoadSubagents(int chatId)
    {
        await using var context=new HarnessDb();
        var children=await context.Subagents.AsNoTracking().Where(x=>x.ChatId==chatId).OrderBy(x=>x.CreatedUtc).ToListAsync();
        if(chat?.Id!=chatId)return;
        foreach(var child in children)
        {
            if(child.Status=="running" && !conversationRuns.ContainsKey(chatId))child.Status="interrupted";
            subagentViews[child.Id]=child;
            if(!messages.Children.OfType<Button>().Any(x=>Equals(x.Tag,child.Id)))messages.Children.Add(ChildBubble(child));
        }
        RefreshSubagentSidebar();
    }
    void RefreshSubagentSidebar()
    {
        static StackPanel? Find(DependencyObject node)
        {
            if(node is StackPanel p && Equals(p.Tag,"subagents"))return p;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find(VisualTreeHelper.GetChild(node,i)) is {} child)return child;
            return null;
        }
        foreach(var row in chats.Items.OfType<Chat>())
        {
            if(chats.ContainerFromItem(row) is not ListViewItem container || Find(container) is not {} panel)continue;
            panel.Children.Clear();
            foreach(var child in subagentViews.Values.Where(x=>x.ChatId==row.Id && x.Status=="running"))
            {
                var button=new Button {Content="↳ "+child.Name+"\n"+child.Activity,Margin=new Thickness(12,0,0,0),FontSize=11};
                button.Click+=async(_,e)=>{if(chat?.Id!=row.Id){var previousLoading=loading;loading=true;chats.SelectedItem=row;loading=previousLoading;await SelectChat();}OpenSubagent(child.Id);};panel.Children.Add(button);
            }
        }
    }
}

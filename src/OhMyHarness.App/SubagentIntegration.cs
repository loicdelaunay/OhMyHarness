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
        foreach(var button in run.Messages.Children.OfType<Button>().Where(x=>Equals(x.Tag,child.Id)))button.Content=ChildCard(child, false);
        RefreshSubagentSidebar();
        if(selectedSubagent==child.Id)RenderSubagent(child);
        else if(IsVisible(run))ScrollToBottom();
    }
    string ChildStatus(SubagentRecord child) => (child.Status, state.Language == "en") switch
    {
        ("running", false) => "En cours", ("running", true) => "Running",
        ("completed", false) => "Terminé", ("completed", true) => "Completed",
        ("failed", false) => "Échec", ("failed", true) => "Failed",
        ("limited", false) => "Limite atteinte", ("limited", true) => "Limit reached",
        ("cancelled", false) => "Annulé", ("cancelled", true) => "Cancelled",
        (_, false) => "Interrompu", (_, true) => "Interrupted"
    };
    string ChildActivity(SubagentRecord child)
    {
        var activity = child.Activity;
        foreach (var pair in new[] { ("Réflexion / Thinking", "Réflexion", "Thinking"), ("Démarrage / Starting", "Démarrage", "Starting"), ("Réponse reçue / Response received", "Réponse reçue", "Response received"), ("Outil / Tool", "Outil", "Tool") })
            activity = activity.Replace(pair.Item1, state.Language == "en" ? pair.Item3 : pair.Item2);
        return child.Status == "running" ? activity : ChildStatus(child);
    }
    FrameworkElement ChildCard(SubagentRecord child, bool compact)
    {
        var content = new Grid { ColumnSpacing = 8, RowSpacing = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var color = child.Status == "failed" ? Brush(255, 145, 145) : child.Status == "completed" ? Brush(110, 220, 150) : Brush(130, 180, 255);
        var icon = new FontIcon { Glyph = child.Status == "completed" ? "\uE73E" : child.Status == "failed" ? "\uE783" : "\uE8D7", FontSize = 13, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(icon);
        var name = new TextBlock { Text = child.Name, FontSize = compact ? 12 : 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = FluentDesign.Primary };
        Grid.SetColumn(name, 1); content.Children.Add(name);
        var step = System.Text.RegularExpressions.Regex.Match(child.Activity, @"^Étape (\d+/\d+) · ");
        var badge = new Border { Background = FluentDesign.Card, CornerRadius = new(4), Padding = new(5, 2, 5, 2), Child = new TextBlock { Text = compact ? (step.Success ? step.Groups[1].Value : "›") : ChildStatus(child), FontSize = 10, Foreground = color } };
        Grid.SetColumn(badge, 2); content.Children.Add(badge);
        var activity = new TextBlock { Text = step.Success ? ChildActivity(child)[step.Length..] : ChildActivity(child), FontSize = 11, Foreground = FluentDesign.Secondary, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(activity, 1); Grid.SetColumn(activity, 1); Grid.SetColumnSpan(activity, 2); content.Children.Add(activity);
        if (!compact)
        {
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var task = new TextBlock { Text = child.Task, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Foreground = FluentDesign.Secondary, Margin = new(0, 4, 0, 0) };
            Grid.SetRow(task, 2); Grid.SetColumn(task, 1); Grid.SetColumnSpan(task, 2); content.Children.Add(task);
        }
        ToolTipService.SetToolTip(content, child.Name + "\n" + ChildActivity(child) + "\n" + child.Task);
        return content;
    }
    Button ChildBubble(SubagentRecord child)
    {
        var button=new Button {Tag=child.Id,Content=ChildCard(child, false),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(14),CornerRadius=new(10),BorderBrush=FluentDesign.Stroke,Background=FluentDesign.Card};
        button.Click+=(_,e)=> {OpenSubagent(child.Id);};return button;
    }
    void OpenSubagent(string id)
    {
        if(!subagentViews.TryGetValue(id,out var child))return;
        selectedSubagent=id;childPanel=CreateMessagePanel();scroll.Content=childPanel;followChatTail=true;
        pinnedTasks.Visibility=Visibility.Collapsed;
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
                var button=new Button {Content=ChildCard(child, true),Margin=new Thickness(0),FontSize=11,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8),CornerRadius=new(6),BorderThickness=new(0),Background=selectedSubagent==child.Id ? FluentDesign.Card : new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, child.Name + ", " + ChildActivity(child));
                button.Click+=async(_,e)=>{if(chat?.Id!=row.Id){var previousLoading=loading;loading=true;chats.SelectedItem=row;loading=previousLoading;await SelectChat();}OpenSubagent(child.Id);};panel.Children.Add(button);
            }
        }
    }
}

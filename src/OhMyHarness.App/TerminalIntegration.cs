using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly TerminalHub terminals = new();
    readonly TabView terminalTabs = new() { IsAddTabButtonVisible = true, TabWidthMode = TabViewWidthMode.SizeToContent };
    sealed record TerminalUi(int ChatId, TabViewItem Tab, TextBlock Info, TextBox Command, TextBox Output, Button Run, Button Stop);
    readonly Dictionary<string, TerminalUi> terminalViews = [];
    readonly DispatcherTimer terminalRefresh = new() { Interval = TimeSpan.FromMilliseconds(400) };
    int? terminalVisibleChat;

    FrameworkElement BuildTerminals()
    {
        terminalTabs.AddTabButtonClick += async (_, _) => await Guard(() => {
            if (chat == null) throw new InvalidOperationException("Sélectionnez une conversation.");
            var created = terminals.Create(chat.Id, false, "", RequireDirectory());
            RefreshTerminals(); terminalTabs.SelectedItem = terminalViews[created.Id].Tab;
            return Task.CompletedTask;
        });
        terminalTabs.TabCloseRequested += async (_, e) => await Guard(async () => {
            var id = (string)e.Tab.Tag;
            var view = terminalViews[id];
            await terminals.DeleteAsync(view.ChatId, null, id);
            RefreshTerminals();
        });
        terminalRefresh.Tick += (_, _) => RefreshTerminals();
        terminalTabs.Loaded += (_, _) => { RefreshTerminals(); terminalRefresh.Start(); };
        terminalTabs.Unloaded += (_, _) => terminalRefresh.Stop();
        return terminalTabs;
    }
    void RefreshTerminals()
    {
        var rows = chat == null ? [] : terminals.List(chat.Id);
        if (terminalVisibleChat != chat?.Id) { terminalTabs.TabItems.Clear(); terminalVisibleChat = chat?.Id; }
        foreach (var item in terminalTabs.TabItems.OfType<TabViewItem>().ToList())
            if (!rows.Any(x => x.Id == (string)item.Tag)) { terminalTabs.TabItems.Remove(item); terminalViews.Remove((string)item.Tag); }
        foreach (var row in rows)
        {
            if (!terminalViews.TryGetValue(row.Id, out var ui))
            {
                var panel = new Grid { RowSpacing = 8, Padding = new Thickness(4) };
                foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) panel.RowDefinitions.Add(new() { Height = height });
                var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
                var output = OutputBox();
                var command = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 140, PlaceholderText = row.Shell + "…" };
                var start = new Button { Content = WorkflowText("Exécuter", "Run") };
                var stop = new Button { Content = WorkflowText("Arrêter", "Stop") };
                start.Click += async (_, _) => await Guard(() => {
                    var sourceProject = db.Projects.Local.FirstOrDefault(p => p.Id == db.Chats.Local.FirstOrDefault(c => c.Id == row.ChatId)?.ProjectId);
                    if (sourceProject == null || !sourceProject.GetSourceFolders().Any(path => PlatformSupport.PathComparer.Equals(Path.GetFullPath(path), row.Directory))) throw new UnauthorizedAccessException("Dossier détaché du projet.");
                    terminals.Start(row.ChatId, false, row.Id, command.Text, (text, append, ct) => WorkspaceTools.ShellAsync(text, row.Directory, ct, append), CancellationToken.None);
                    RefreshTerminals(); return Task.CompletedTask;
                });
                stop.Click += async (_, _) => await Guard(() => { terminals.Stop(row.ChatId, null, row.Id); return Task.CompletedTask; });
                panel.Children.Add(info); Grid.SetRow(output, 1); panel.Children.Add(output); Grid.SetRow(command, 2); panel.Children.Add(command);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(start); actions.Children.Add(stop); Grid.SetRow(actions, 3); panel.Children.Add(actions);
                var tab = new TabViewItem { Tag = row.Id, Content = panel };
                ui = new(row.ChatId, tab, info, command, output, start, stop); terminalViews[row.Id] = ui;
            }
            if (!terminalTabs.TabItems.Contains(ui.Tab)) terminalTabs.TabItems.Add(ui.Tab);
            ui.Tab.Header = row.Name + (row.Status == "running" ? " ●" : "") + (row.Sandbox ? " · Sandbox" : "");
            ui.Info.Text = row.Shell + " · " + row.Status + "\n" + row.Directory + "\n" + WorkflowText("Commande indépendante · 60 s max · utilisez + pour exécuter en parallèle.", "Independent command · 60s max · use + to run in parallel.");
            var text = (row.Command.Length == 0 ? "" : "> " + row.Command + "\n") + row.Output;
            if (ui.Output.Text != text) ui.Output.Text = text;
            ui.Run.IsEnabled = row.Status != "running" && !row.Sandbox;
            ui.Stop.IsEnabled = row.Status == "running";
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;


namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed class MessageActionMenu(Button button, MenuFlyout menu)
    {
        public Button Button { get; } = button;
        public MenuFlyout Menu { get; } = menu;
        public bool HasCopy { get; set; }
        public bool HasBranches { get; set; }
    }

    MessageActionMenu EnsureMessageActionMenu(Border card)
    {
        if (card.Tag is MessageActionMenu existing) return existing;
        var stack = card.Child as StackPanel ?? throw new InvalidOperationException("Message card has no content.");
        var button = new Button { Width = 30, Height = 30, Padding = new(0), Visibility = Visibility.Collapsed,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
        FluentDesign.IconButton(button, "\uE712", WorkflowText("Actions du message", "Message actions"), false);
        var menu = new MenuFlyout();
        bool menuOpen = false, pointerInside = false;
        button.Click += (_, _) => { menuOpen = true; menu.ShowAt(button); };
        menu.Closed += (_, _) => { menuOpen = false; button.Visibility = pointerInside ? Visibility.Visible : Visibility.Collapsed; };
        var header = (FrameworkElement)stack.Children[0]; stack.Children.RemoveAt(0);
        var heading = new Grid { MinHeight = 30 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(header, 0); Grid.SetColumn(button, 1);
        heading.Children.Add(header); heading.Children.Add(button); stack.Children.Insert(0, heading);
        card.PointerEntered += (_, _) => { pointerInside = true; button.Visibility = Visibility.Visible; };
        card.PointerExited += (_, e) =>
        {
            var point = e.GetCurrentPoint(card).Position;
            if (point.X >= 0 && point.Y >= 0 && point.X <= card.ActualWidth && point.Y <= card.ActualHeight) return;
            pointerInside = false;
            if (!menuOpen) button.Visibility = Visibility.Collapsed;
        };
        var actions = new MessageActionMenu(button, menu);
        card.Tag = actions;
        return actions;
    }

    void AddMessageCopyAction(MessageActionMenu actions, Func<string> text)
    {
        if (actions.HasCopy) return;
        actions.HasCopy = true;
        var copy = new MenuFlyoutItem { Text = UiText.T("Copier"), Icon = FluentDesign.Icon("\uE8C8") };
        copy.Click += async (_, _) => await Guard(() =>
        {
            var data = new DataPackage();
            data.SetText(text());
            Clipboard.SetContent(data);
            ShowStatus(UiText.T("Copié !"), StatusKind.Notice);
            return Task.CompletedTask;
        });
        actions.Menu.Items.Insert(0, copy);
    }

    Grid StatusGlow(Border chip)
    {
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        var glow = new Grid(); host.Children.Add(glow); host.Children.Add(chip);
#if WINDOWS
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(glow);
        var sprite = visual.Compositor.CreateSpriteVisual();
        var shadow = visual.Compositor.CreateDropShadow(); shadow.BlurRadius = 22; shadow.Opacity = .38f;
        var accent = (Microsoft.UI.Xaml.Media.SolidColorBrush)FluentDesign.Resource("AccentFillColorDefaultBrush");
        shadow.Color = accent.Color; sprite.Shadow = shadow;
        var glowBrush = visual.Compositor.CreateColorBrush(accent.Color); sprite.Brush = glowBrush;
        var accentCallback = accent.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.Media.SolidColorBrush.ColorProperty, (_, _) => { shadow.Color = accent.Color; glowBrush.Color = accent.Color; });
        Closed += (_, _) => accent.UnregisterPropertyChangedCallback(Microsoft.UI.Xaml.Media.SolidColorBrush.ColorProperty, accentCallback);
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(glow, sprite);
        host.SizeChanged += (_, e) => { sprite.Size = new System.Numerics.Vector2((float)Math.Max(0,e.NewSize.Width-44), (float)Math.Max(0,e.NewSize.Height-14)); sprite.Offset = new System.Numerics.Vector3(22,7,0); };
#else
        for (int i=6; i>0; i--) glow.Children.Add(new Border { Background=FluentDesign.Resource("ActivityGlowBrush"), CornerRadius=new(24), Margin=new(-i*2,-i,-i*2,-i) });
#endif
        ConnectStatusGlow(chip, glow);
        chip.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => host.Visibility = chip.Visibility);
        host.Visibility = chip.Visibility; return host;
    }
    readonly AsyncLocal<Project?> permissionProject = new();
    readonly AsyncLocal<ConversationRun?> automaticToolRun = new();
    async Task SaveConversationResourcesAsync(IEnumerable<string> paths)
    {
        if (chat == null || project == null) return;
        var validated = ProjectResources.Validate(paths);
        chat.ResourcePathsJson = ProjectResources.Serialize(validated);
        project.SetSourceFolders(validated);
        await db.SaveChangesAsync();
        UpdateSourceLabel(); UpdateFloatingAssets(); ResetWorkspaceTools();
        ShowStatus(WorkflowText("Ressources associées à cette conversation · disponibles au prochain envoi.", "Resources attached to this conversation · available on the next send."));
    }
    void EnableResourceDrop(UIElement target)
    {
        target.AllowDrop = true;
        target.DragOver += (_, e) => { if (e.DataView.Contains(StandardDataFormats.StorageItems)) { e.AcceptedOperation = DataPackageOperation.Copy; e.Handled = true; } };
        target.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var deferred = e.GetDeferral(); e.Handled = true;
            try { await Guard(async () => { var items = await e.DataView.GetStorageItemsAsync(); await SaveConversationResourcesAsync((project?.GetSourceFolders() ?? []).Concat(items.Select(x => x.Path))); }); }
            finally { deferred.Complete(); }
        };
    }
    async Task AttachFilesAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*");
        InitializePicker(picker, this);
        var selected = await picker.PickMultipleFilesAsync();
        if (selected.Count > 0) await SaveConversationResourcesAsync((project?.GetSourceFolders() ?? []).Concat(selected.Select(x => x.Path)));
    }
    void AddHistoryActions(Message message, Border? card)
    {
        if (card?.Child is not StackPanel || message.Role is not ("user" or "assistant")) return;
        var actions = EnsureMessageActionMenu(card);
        if (message.Role == "user") AddMessageCopyAction(actions, () => message.Content);
        if (message.State is not ("complete" or "compacted") || actions.HasBranches) return;
        if (message.WireJson.Length > 0 && System.Text.Json.Nodes.JsonNode.Parse(message.WireJson)?["tool_calls"] is System.Text.Json.Nodes.JsonArray { Count: > 0 }) return;
        async Task Branch(bool resume)
        {
            if (resume)
            {
                if (conversationRuns.ContainsKey(message.ChatId)) throw new InvalidOperationException(WorkflowText("Arrêtez la génération avant de reprendre plus haut.", "Stop generation before resuming here."));
                if (await ShowDialogAsync(new ContentDialog { XamlRoot = root.XamlRoot, Title = WorkflowText("Reprendre ici ?", "Resume here?"), Content = WorkflowText("La suite sera conservée dans une conversation Sauvegarde. Cette conversation reprendra à ce message et sa file d’attente sera vidée.", "Later messages will be kept in a Backup conversation. This chat will resume here and its queue will be cleared."), PrimaryButtonText = WorkflowText("Reprendre", "Resume"), CloseButtonText = WorkflowText("Annuler", "Cancel") }) != ContentDialogResult.Primary) return;
            }
            var branch = await ConversationBranches.CreateAsync(HarnessDb.DatabasePath, message.ChatId, message.Id, resume);
            if (resume) foreach (var id in subagentViews.Where(x => x.Value.ChatId == message.ChatId).Select(x => x.Key).ToArray()) subagentViews.Remove(id);
            conversationHistory.Remove(message.ChatId);
            state.ChatId = branch.Id; await db.SaveChangesAsync(); await SelectProject();
            composer.Focus(FocusState.Programmatic);
        }
        var fork = new MenuFlyoutItem { Text = WorkflowText("Créer un fork", "Fork from here") };
        var resume = new MenuFlyoutItem { Text = WorkflowText("Reprendre ici", "Resume here") };
        fork.Click += async (_, _) => await Guard(() => Branch(false));
        resume.Click += async (_, _) => await Guard(() => Branch(true));
        actions.Menu.Items.Add(fork); actions.Menu.Items.Add(resume);
        actions.HasBranches = true;
    }
    async Task FocusLatestToolAsync(ConversationRun run, string name, System.Text.Json.Nodes.JsonObject? args = null)
    {
        if (!IsVisible(run) || !FeatureSettings.Read(state.FeaturesJson).AutoFocusTool) return;
        int tab = name.StartsWith("asset_",StringComparison.Ordinal) ? 4 : name.Contains("terminal") ? 1 : name.StartsWith("git") ? 2 : name.Contains("source") || name.StartsWith("rag_") ? 3 : name.Contains("browser") || name is "browse" or "read_page" or "inspect_dom" or "open_local_file" ? 0 : -1;
        if (tab >= 0)
        {
            browserVisible = true; browserPanel.Visibility = Visibility.Visible;
            toolTabs.SelectedIndex = tab; SyncBrowserPresentation(); ResizeLayout();
            try
            {
                await ActivateToolAsync();
                if (tab == 1) SelectTerminalTab(run, args?["terminal_id"]?.GetValue<string>(), name == "run_terminal");
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { SetRunStatus(run, ex.Message, StatusKind.Error); }
        }
    }
}

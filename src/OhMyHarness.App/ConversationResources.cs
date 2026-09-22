using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;


namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    Grid StatusGlow(Border chip)
    {
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        var glow = new Grid(); host.Children.Add(glow); host.Children.Add(chip);
#if WINDOWS
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(glow);
        var sprite = visual.Compositor.CreateSpriteVisual();
        var shadow = visual.Compositor.CreateDropShadow(); shadow.BlurRadius = 22; shadow.Opacity = .38f;
        shadow.Color = Windows.UI.Color.FromArgb(255, 65, 165, 245); sprite.Shadow = shadow;
        sprite.Brush = visual.Compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 65, 165, 245));
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(glow, sprite);
        host.SizeChanged += (_, e) => { sprite.Size = new System.Numerics.Vector2((float)Math.Max(0,e.NewSize.Width-44), (float)Math.Max(0,e.NewSize.Height-14)); sprite.Offset = new System.Numerics.Vector3(22,7,0); };
#else
        for (int i=6; i>0; i--) glow.Children.Add(new Border { Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(6,65,165,245)), CornerRadius=new(24), Margin=new(-i*2,-i,-i*2,-i) });
#endif
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
        status.Text = WorkflowText("Ressources associées à cette conversation · disponibles au prochain envoi.", "Resources attached to this conversation · available on the next send.");
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
    void AddHistoryActions(Message message, StackPanel target)
    {
        if (message.State is not ("complete" or "compacted") || message.Role is not ("user" or "assistant")) return;
        if (message.WireJson.Length > 0 && System.Text.Json.Nodes.JsonNode.Parse(message.WireJson)?["tool_calls"] is System.Text.Json.Nodes.JsonArray { Count: > 0 }) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 6, Margin = new(0, -8, 4, 4) };
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
        row.Children.Add(Action(WorkflowText("Créer un fork", "Fork here"), () => Branch(false)));
        row.Children.Add(Action(WorkflowText("Reprendre ici", "Resume here"), () => Branch(true)));
        target.Children.Add(row);
    }
    async Task FocusLatestToolAsync(ConversationRun run, string name)
    {
        if (!IsVisible(run) || !FeatureSettings.Read(state.FeaturesJson).AutoFocusTool) return;
        int tab = name.Contains("terminal") ? 1 : name.StartsWith("git") ? 2 : name.Contains("source") || name.StartsWith("rag_") ? 3 : name.Contains("browser") || name is "browse" or "read_page" or "inspect_dom" or "open_local_file" ? 0 : -1;
        if (tab >= 0)
        {
            browserVisible = true; browserPanel.Visibility = Visibility.Visible;
            toolTabs.SelectedIndex = tab; SyncBrowserPresentation(); ResizeLayout();
            try { await ActivateToolAsync(); } catch (Exception ex) when (ex is not OperationCanceledException) { SetRunStatus(run, ex.Message); }
        }
    }
}

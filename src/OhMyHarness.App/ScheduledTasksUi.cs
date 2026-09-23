using Microsoft.EntityFrameworkCore;
using OhMyHarness.Core;
using Windows.Storage.Pickers;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    TaskSchedulerService? scheduler;
    readonly DispatcherTimer scheduleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    Window? tasksWindow;
    void StartScheduler()
    {
        scheduler = new(HarnessDb.DatabasePath, ExecuteScheduledTask);
        scheduleTimer.Tick += async (_, _) =>
        {
            try { await scheduler.TickAsync(DateTime.UtcNow); }
            catch (Exception ex) { ShowStatus("Planification : " + ex.Message, StatusKind.Error); }
        };
        scheduleTimer.Start();
        Closed += (_, _) => { scheduleTimer.Stop(); scheduler.Dispose(); tasksWindow?.Close(); };
    }
    async Task<string> ExecuteScheduledTask(ScheduledTask task, CancellationToken ct)
    {
        var input=await ScheduledTaskInput.PrepareAsync(HarnessDb.DatabasePath,task,id=>conversationRuns.ContainsKey(id),ct);
        var conversation=input.Chat;var owner=input.Project;var target=input.Provider;var options=input.Options;var configured=input.Providers;
        // A visible history panel must finish materializing before an agent appends to it.
        while (conversationLoading && chat?.Id == conversation.Id) await Task.Delay(20, ct);
        if (conversationRuns.ContainsKey(conversation.Id)) throw new InvalidOperationException("Conversation déjà en cours : cette échéance est ignorée.");
        var visible = chat?.Id == conversation.Id && selectedSubagent == null;
        var panel = visible ? messages : CreateMessagePanel();
        var run = new ConversationRun(conversation, owner, target, options, task.Instruction, [], configured) { Messages = panel, IsScheduled = true };
        conversationRuns.Add(conversation.Id, run);
        using var cancellation = ct.Register(run.Cancellation.Cancel);
        try
        {
            if (!visible)
            {
                var history=await ReadStoreAsync(store => store.Messages.AsNoTracking().Include(x=>x.Attachments).Where(x=>x.ChatId==conversation.Id).OrderBy(x=>x.Id).ToList(), ct);
                for (var i = 0; i < history.Count; i++)
                {
                    await Task.Delay(1, ct);
                    RenderHistory(history, panel, run.Project, i, 1);
                }
            }
            if (project?.Id == owner.Id)
            {
                var items = await db.Chats.Where(x => x.ProjectId == owner.Id).OrderByDescending(x => x.Id).ToListAsync(ct);
                if (project?.Id == owner.Id)
                {
                    allProjectChats = items;
                    ApplyChatSearch(chat?.Id);
                }
            }
        }
        catch { conversationRuns.Remove(conversation.Id); run.Dispose(); throw; }
        await ExecuteRunAsync(run);
        return run.Failed ? run.Status : "Terminée / Completed";
    }
    async Task ShowScheduledTasks()
    {
        if (project == null) return;
        if (tasksWindow != null) { tasksWindow.Activate(); return; }
        var projectId = project.Id;
        var window = new Window { Title = DisplayApplicationName + " · " + WorkflowText("Tâches planifiées", "Scheduled tasks") };
        tasksWindow = window;
        var body = new StackPanel { Spacing = 14, Padding = new(24), MaxWidth = 850, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scroller = new ScrollViewer { Content = body, RequestedTheme = root.RequestedTheme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        FluentDesign.WindowChrome(window);
        ApplyBrandingIcon(window);
        window.Content = scroller; window.AppWindow.Resize(new() { Width = 960, Height = 880 });
        ObserveTextZoom(scroller);
        window.Closed += (_, _) => tasksWindow = null;
        window.Activate();
        var list = new StackPanel { Spacing = 10 };
        var form = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        var heading = Label(WorkflowText("Tâches du projet · ", "Project tasks · ") + project.Name, 22);
        var explanation = Label(WorkflowText("Les tâches s’exécutent pendant que l’application est ouverte, y compris dans les réglages. Une échéance manquée est rattrapée une seule fois à la réouverture. Une même tâche ne se chevauche pas.", "Tasks run while the app is open, including in settings. Missed occurrences are coalesced into one run on restart. A task never overlaps itself."), 12);
        var add = new Button { Content = WorkflowText("＋ Nouvelle tâche", "＋ New task") };
        body.Children.Add(heading); body.Children.Add(explanation); body.Children.Add(add); body.Children.Add(list); body.Children.Add(form);
        var allProviders = await db.Providers.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        ScheduledTask? draft = null;
        var name = new TextBox { Header = WorkflowText("Nom", "Name") };
        var prompt = new TextBox { Header = WorkflowText("Instruction", "Instruction"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 240 };
        var connection = new ComboBox { Header = WorkflowText("Fournisseur", "Provider"), ItemsSource = allProviders, HorizontalAlignment = HorizontalAlignment.Stretch };
        var model = new ComboBox { Header = "Modèle / Model", HorizontalAlignment = HorizontalAlignment.Stretch };
        var thinking = new ComboBox { Header = "Réflexion / Reasoning", ItemsSource = new[] { "auto", "none", "low", "medium", "high" } };
        var context = new NumberBox { Header = "Contexte (tokens) / Context", Minimum = 1024, Maximum = 10_000_000 };
        var images = new CheckBox { Content = "Modèle compatible images / Image-capable model" };
        var period = new ComboBox { Header = "Fréquence / Frequency", ItemsSource = new[] { "Toutes les X minutes / Every X minutes", "Toutes les X heures / Every X hours", "Chaque jour / Daily", "Chaque semaine / Weekly", "CRON personnalisé / Custom CRON" }, SelectedIndex = 0 };
        var interval = new NumberBox { Header = "Intervalle / Interval", Minimum = 1, Maximum = 59, Value = 30, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var time = new TimePicker { Header = "Heure / Time", ClockIdentifier = "24HourClock", Time = TimeSpan.FromHours(9) };
        var weekday = new ComboBox { Header = "Jour / Day", ItemsSource = new[] { "Dimanche / Sunday", "Lundi / Monday", "Mardi / Tuesday", "Mercredi / Wednesday", "Jeudi / Thursday", "Vendredi / Friday", "Samedi / Saturday" }, SelectedIndex = 1 };
        var cron = new TextBox { Header = "CRON · minute heure jour mois semaine", Text = "*/30 * * * *" };
        var zones = TimeZoneInfo.GetSystemTimeZones().ToList();
        var zone = new ComboBox { Header = "Fuseau horaire / Time zone", ItemsSource = zones, DisplayMemberPath = "DisplayName", HorizontalAlignment = HorizontalAlignment.Stretch };
        var next = Label("", 12); next.Tag = null;
        var inherit = new CheckBox { Content = "Utiliser les dossiers par défaut du projet / Inherit project folders" };
        var folders = new TextBox { Header = "Fichiers et dossiers (un par ligne) / Resources (one per line)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
        var browse = new Button { Content = "＋ Dossier / Folder" };
        browse.Click += async (_, _) => { var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); InitializePicker(picker, window); var folder = await picker.PickSingleFolderAsync(); if (folder != null) { inherit.IsChecked = false; folders.Text = (folders.Text + "\n" + folder.Path).Trim(); } };
        inherit.Checked += (_, _) => folders.IsEnabled = browse.IsEnabled = false;
        inherit.Unchecked += (_, _) => folders.IsEnabled = browse.IsEnabled = true;
        var memory = new CheckBox { Content = "Continuer l’historique à chaque exécution / Continue conversation history" };
        var enabled = new CheckBox { Content = "Tâche active / Enabled" };
        var mode = new ComboBox { Header = "Mode", ItemsSource = new[] { "execute", "plan" } };
        var orchestration = new ComboBox { Header = "Sous-agents / Subagents", ItemsSource = new[] { "disabled", "auto", "forced" } };
        var sandbox = new CheckBox { Content = "Sandbox" };
        var autoContinue = new CheckBox { Content = "Continuer automatiquement / Auto-continue" };
        var skills = new StackPanel { Spacing = 2 };
        var skillChecks = new List<(CheckBox Check, string Id)>();
        var error = Label("", 12); error.Tag = null;
        bool editing = false;
        void Preview()
        {
            try
            {
                var probe = new ScheduledTask { Cron = cron.Text, TimeZoneId = (zone.SelectedItem as TimeZoneInfo)?.Id ?? TimeZoneInfo.Local.Id };
                var cursor = DateTime.UtcNow; var occurrences = new List<string>();
                for (int i=0; i<3; i++) { var value = probe.Next(cursor); if (value == null) break; cursor = value.Value; occurrences.Add(TimeZoneInfo.ConvertTimeFromUtc(cursor, TimeZoneInfo.FindSystemTimeZoneById(probe.TimeZoneId)).ToString("g")); }
                next.Text = "Prochaines exécutions / Next runs: " + string.Join(" · ", occurrences);
            }
            catch (Exception ex) { next.Text = ex.Message; }
        }
        void ComposeCron()
        {
            if (editing) return;
            var n = double.IsFinite(interval.Value) ? Math.Clamp((int)interval.Value, 1, period.SelectedIndex == 1 ? 23 : 59) : 1;
            cron.Text = period.SelectedIndex switch { 0 => $"*/{n} * * * *", 1 => $"0 */{n} * * *", 2 => $"{time.Time.Minutes} {time.Time.Hours} * * *", 3 => $"{time.Time.Minutes} {time.Time.Hours} * * {weekday.SelectedIndex}", _ => cron.Text };
            interval.Visibility = period.SelectedIndex < 2 ? Visibility.Visible : Visibility.Collapsed;
            interval.Maximum = period.SelectedIndex == 1 ? 23 : 59;
            time.Visibility = period.SelectedIndex is 2 or 3 ? Visibility.Visible : Visibility.Collapsed;
            weekday.Visibility = period.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            cron.IsReadOnly = period.SelectedIndex != 4;
            Preview();
        }
        period.SelectionChanged += (_, _) => ComposeCron(); interval.ValueChanged += (_, _) => ComposeCron(); time.TimeChanged += (_, _) => ComposeCron(); weekday.SelectionChanged += (_, _) => ComposeCron();
        cron.TextChanged += (_, _) => Preview(); zone.SelectionChanged += (_, _) => Preview();
        connection.SelectionChanged += (_, _) =>
        {
            if (connection.SelectedItem is not Provider selected) return;
            var choices = ProviderModels.Visible(selected);
            model.ItemsSource = choices;
            model.SelectedItem = choices.FirstOrDefault();
            if (!editing) { context.Value = selected.ContextLimit; images.IsChecked = selected.SupportsImages; }
        };
        async Task Reload()
        {
            using var store = new HarnessDb();
            list.Children.Clear();
            foreach (var item in await store.ScheduledTasks.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Name).ToListAsync())
            {
                var edit = new Button { Content = "Modifier / Edit" }; edit.Click += (_, _) => Edit(item);
                var toggle = new ToggleSwitch { IsOn = item.Enabled, OnContent = "Active", OffContent = "Pause" };
                toggle.Toggled += async (_, _) => { using var update = new HarnessDb(); var active = toggle.IsOn; var due = item.Next(DateTime.UtcNow); await update.ScheduledTasks.Where(x=>x.Id==item.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enabled,active).SetProperty(x=>x.NextRunUtc,due)); };
                var delete = new Button { Content = "Supprimer / Delete" };
                delete.Click += async (_, _) =>
                {
                    var confirm = new ContentDialog { XamlRoot = scroller.XamlRoot, Title = "Supprimer la tâche ? / Delete task?", Content = item.Name, PrimaryButtonText = "Supprimer / Delete", CloseButtonText = "Annuler / Cancel" };
                    if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                    using var update = new HarnessDb(); await update.ScheduledTasks.Where(x=>x.Id==item.Id).ExecuteDeleteAsync(); await Reload();
                };
                var detail = new StackPanel { Spacing = 8 }; detail.Children.Add(Label(item.Cron + " · " + item.TimeZoneId,12));
                detail.Children.Add(Label((item.NextRunUtc?.ToLocalTime().ToString("g") ?? "—") + " · " + item.LastResult,12)); detail.Children.Add(Row(edit,delete));
                list.Children.Add(FluentDesign.Setting(item.Name,item.Model,toggle,detail));
            }
        }
        void Edit(ScheduledTask value)
        {
            draft = value; editing = true; error.Text = "";
            name.Text=value.Name; prompt.Text=value.Instruction; connection.SelectedItem=allProviders.FirstOrDefault(x=>x.Id==value.ProviderId);
            if (connection.SelectedItem is Provider chosen) { var choices = ProviderModels.Visible(chosen); model.ItemsSource=ProviderModels.Normalize(choices.Concat([value.Model])); model.SelectedItem=value.Model; }
            thinking.SelectedItem=value.ThinkingLevel; context.Value=value.ContextLimit; images.IsChecked=value.SupportsImages;
            period.SelectedIndex=value.Id==0 ? 0 : 4; cron.Text=value.Cron; zone.SelectedItem=zones.FirstOrDefault(x=>x.Id==value.TimeZoneId) ?? zones.FirstOrDefault(x=>x.Id==TimeZoneInfo.Local.Id);
            inherit.IsChecked=value.ResourcePathsJson.Length==0; folders.Text=string.Join('\n',ProviderModels.Parse(value.ResourcePathsJson));
            memory.IsChecked=value.RememberHistory; enabled.IsChecked=value.Enabled; mode.SelectedItem=value.ExecutionMode; orchestration.SelectedItem=value.OrchestrationMode; sandbox.IsChecked=value.SandboxEnabled; autoContinue.IsChecked=value.AutoContinue;
            skills.Children.Clear(); skillChecks.Clear();
            foreach (var skill in Skills.Available(project?.GetSourceFolders(), project?.Id ?? 0))
            {
                var check = new CheckBox { Content = state.Language=="en"?skill.EnglishName:skill.FrenchName, IsChecked = Skills.Enabled(value.EnabledSkills,skill.Id) };
                skillChecks.Add((check,skill.Id)); skills.Children.Add(check);
            }
            editing=false; ComposeCron(); form.Visibility=Visibility.Visible; list.Visibility=Visibility.Collapsed; add.Visibility=Visibility.Collapsed;
        }
        add.Click += (_, _) => Edit(new ScheduledTask { ProjectId=projectId, ProviderId=provider?.Id ?? 0, Model=provider?.Model ?? "", ContextLimit=provider?.ContextLimit ?? 128000, SupportsImages=provider?.SupportsImages ?? false, ThinkingLevel=state.ThinkingLevel, EnabledSkills=state.EnabledSkills, AutoContinue=state.AutoContinue });
        var save = new Button { Content = "Enregistrer / Save", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancel = new Button { Content = "Annuler / Cancel" };
        cancel.Click += async (_, _) => { form.Visibility=Visibility.Collapsed; list.Visibility=add.Visibility=Visibility.Visible; await Reload(); };
        save.Click += async (_, _) =>
        {
            if (draft == null) return;
            try
            {
                draft.Name=name.Text.Trim(); draft.Instruction=prompt.Text.Trim(); draft.ProviderId=(connection.SelectedItem as Provider)?.Id ?? 0; draft.Model=model.SelectedItem as string ?? "";
                draft.ThinkingLevel=thinking.SelectedItem as string ?? "auto"; draft.ContextLimit=(int)context.Value; draft.SupportsImages=images.IsChecked==true;
                draft.Cron=cron.Text.Trim(); draft.TimeZoneId=(zone.SelectedItem as TimeZoneInfo)?.Id ?? TimeZoneInfo.Local.Id;
                draft.ResourcePathsJson=inherit.IsChecked==true ? "" : ProjectResources.Serialize(ProjectResources.Validate(folders.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)));
                draft.RememberHistory=memory.IsChecked==true; draft.Enabled=enabled.IsChecked==true; draft.ExecutionMode=mode.SelectedItem as string ?? "execute"; draft.OrchestrationMode=orchestration.SelectedItem as string ?? "disabled";
                draft.SandboxEnabled=sandbox.IsChecked==true; draft.AutoContinue=autoContinue.IsChecked==true; draft.EnabledSkills=string.Join(',',skillChecks.Where(x=>x.Check.IsChecked==true).Select(x=>x.Id));
                draft.Validate(); draft.NextRunUtc=draft.Next(DateTime.UtcNow);
                using var store=new HarnessDb();
                if(draft.Id==0) store.ScheduledTasks.Add(draft);
                else
                {
                    var existing=await store.ScheduledTasks.SingleAsync(x=>x.Id==draft.Id);
                    var lastChat=existing.LastChatId; var lastRun=existing.LastRunUtc; var lastResult=existing.LastResult;
                    store.Entry(existing).CurrentValues.SetValues(draft);
                    existing.LastChatId=lastChat; existing.LastRunUtc=lastRun; existing.LastResult=lastResult;
                }
                await store.SaveChangesAsync(); form.Visibility=Visibility.Collapsed; list.Visibility=add.Visibility=Visibility.Visible; await Reload();
            }
            catch(Exception ex) { error.Text=ex.Message; }
        };
        var modelSettings=new StackPanel { Spacing=12 };
        foreach(var control in new UIElement[]{connection,model,thinking,context,images})modelSettings.Children.Add(control);
        var resourceSettings=new StackPanel { Spacing=12 };
        foreach(var control in new UIElement[]{inherit,folders,browse,mode,orchestration,sandbox,autoContinue,new Expander { Header="Skills",Content=skills }})resourceSettings.Children.Add(control);
        foreach(var control in new UIElement[]{name,prompt,period,interval,time,weekday,cron,zone,next,
            new Expander { Header="Modèle et réflexion / Model and reasoning",Content=modelSettings,HorizontalAlignment=HorizontalAlignment.Stretch },
            new Expander { Header="Ressources et outils / Resources and tools",Content=resourceSettings,HorizontalAlignment=HorizontalAlignment.Stretch },
            memory,enabled,error,Row(save,cancel)})form.Children.Add(control);
        await Reload();
    }
}

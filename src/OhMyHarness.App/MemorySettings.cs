using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    FrameworkElement BuildMemorySettings(Dictionary<string, ToggleSwitch> skillToggles)
    {
        var store = new MemoryStore(HarnessDb.DatabasePath);
        var panel = new StackPanel { Spacing = 14, Tag = "memory-settings" };
        panel.Children.Add(Label(WorkflowText(
            "Des informations durables, séparées de l’historique du chat. Conversation : privées au fil. Partagée / Projet : réutilisables dans ce projet. Partagée / Général et Utilisateur : disponibles entre projets.",
            "Durable information, separate from chat history. Conversation: private to the thread. Shared / Project: reusable within this project. Shared / General and User: available across projects."), 13));
        foreach (var (id, fr, en) in new[] { (MemoryTools.ConversationSkill, "Niveau 1 · Mémoire de conversation", "Level 1 · Conversation memory"), (MemoryTools.SharedSkill, "Niveau 2 · Mémoire partagée", "Level 2 · Shared memory") })
        {
            var skill = skillToggles[id];
            var toggle = new ToggleSwitch { IsOn = skill.IsOn, OnContent = WorkflowText("Activée", "Enabled"), OffContent = WorkflowText("Désactivée", "Disabled") };
            toggle.Toggled += (_, _) => { if (skill.IsOn != toggle.IsOn) skill.IsOn = toggle.IsOn; };
            skill.Toggled += (_, _) => { if (toggle.IsOn != skill.IsOn) toggle.IsOn = skill.IsOn; };
            panel.Children.Add(FluentDesign.Setting(WorkflowText(fr, en), WorkflowText("Lecture et recherche ; écritures selon vos autorisations. Lecture seule en mode Plan.", "Read and search; writes follow your permissions. Read-only in Plan mode."), toggle));
        }
        panel.Children.Add(Label(WorkflowText("Les outils mémoire sont proposés aux modèles OpenAI compatibles et à leurs sous-agents. OpenCode conserve ses outils propres. Aucun contenu mémoire n’est injecté en bloc dans le contexte.", "Memory tools are exposed to OpenAI-compatible models and their subagents. OpenCode retains its own tools. Memory contents are not bulk-injected into context."), 12));
        var browser = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
        var progress = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed, Height = 3 };
        var projectPicker = new ComboBox { Header = WorkflowText("Projet", "Project"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var chatPicker = new ComboBox { Header = WorkflowText("Conversation", "Conversation"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var scopePicker = new ComboBox { Header = WorkflowText("Portée", "Scope"), ItemsSource = new[] { WorkflowText("Toutes les portées accessibles", "All accessible scopes"), WorkflowText("Conversation", "Conversation"), WorkflowText("Partagée", "Shared") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var categoryPicker = new ComboBox { Header = WorkflowText("Catégorie", "Category"), ItemsSource = new[] { WorkflowText("Toutes les catégories", "All categories"), WorkflowText("Projet", "Project"), WorkflowText("Général", "General"), WorkflowText("Utilisateur", "User") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var search = new TextBox { PlaceholderText = WorkflowText("Rechercher dans les titres, tags et contenus…", "Search titles, tags and contents…") };
        var results = new StackPanel { Spacing = 8 };
        var editor = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        var scopeEdit = new ComboBox { Tag = "memory-edit-scope", Header = WorkflowText("Portée", "Scope"), ItemsSource = new[] { WorkflowText("Conversation", "Conversation"), WorkflowText("Partagée", "Shared") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var categoryEdit = new ComboBox { Tag = "memory-edit-category", Header = WorkflowText("Catégorie", "Category"), ItemsSource = new[] { WorkflowText("Projet", "Project"), WorkflowText("Général", "General"), WorkflowText("Utilisateur", "User") }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var key = new TextBox { Header = WorkflowText("Clé stable", "Stable key"), PlaceholderText = "preferences.langue", MaxLength = 120 };
        var name = new TextBox { Header = WorkflowText("Titre", "Title"), MaxLength = 200 };
        var tags = new TextBox { Header = "Tags", PlaceholderText = WorkflowText("Ex. architecture, préférences", "E.g. architecture, preferences"), MaxLength = 500 };
        var content = new TextBox { Header = WorkflowText("Information mémorisée", "Saved information"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, MaxHeight = 350, MaxLength = 16000 };
        var provenance = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary, FontSize = 12 };
        MemoryEntry? selected = null;
        MemoryAccess? editingAccess = null;
        bool initializing = true;
        int revision = 0, offset = 0;
        var scopes = new[] { "all", "conversation", "shared" };
        var categories = new[] { "all", "project", "general", "user" };
        Button? more = null;
        CancellationTokenSource? searching = null;
        MemoryAccess Access() => new((projectPicker.SelectedItem as Project)?.Id ?? 0, (chatPicker.SelectedItem as Chat)?.Id);
        async Task Safe(Func<Task> work)
        {
            try { await work(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        void Edit(MemoryEntry? entry)
        {
            selected = entry; editingAccess = Access(); editor.Visibility = Visibility.Visible;
            scopeEdit.SelectedIndex = entry?.Scope == "shared" ? 1 : 0;
            categoryEdit.SelectedIndex = Array.IndexOf(categories, entry?.Category ?? "project") - 1;
            scopeEdit.IsEnabled = categoryEdit.IsEnabled = key.IsEnabled = entry == null;
            key.Text = entry?.Key ?? ""; name.Text = entry?.Title ?? ""; content.Text = entry?.Content ?? ""; tags.Text = entry?.Tags ?? "";
            provenance.Text = entry == null ? WorkflowText("Nouvelle mémoire", "New memory") : $"#{entry.Id} · v{entry.Version} · {entry.Author} · {entry.UpdatedUtc.ToLocalTime():g}" +
                (entry.OriginChatId == null ? "" : WorkflowText(" · Conversation d’origine #", " · Origin conversation #") + entry.OriginChatId);
            DispatcherQueue.TryEnqueue(() => { if (editor.Visibility == Visibility.Visible) editor.StartBringIntoView(); });
        }
        async Task Load(bool append = false)
        {
            searching?.Cancel();
            using var cancellation = new CancellationTokenSource(); searching = cancellation;
            var stamp = ++revision;
            var access = Access(); var scope = scopes[Math.Max(0, scopePicker.SelectedIndex)]; var category = categories[Math.Max(0, categoryPicker.SelectedIndex)];
            var query = search.Text;
            if (!append) { offset = 0; results.Children.Clear(); }
            progress.Visibility = Visibility.Visible; more!.IsEnabled = false;
            try
            {
                var page = await store.SearchAsync(access, query, scope, category, offset, 20, cancellation.Token);
                if (stamp != revision) return;
                if (!append) results.Children.Clear();
                foreach (var hit in page.Items)
                {
                    var card = new StackPanel { Spacing = 4 };
                    card.Children.Add(Label(hit.Title, 15));
                    card.Children.Add(Label($"#{hit.Id} · {ScopeLabel(hit.Scope)} · {CategoryLabel(hit.Category)} · v{hit.Version}", 12));
                    card.Children.Add(new TextBlock { Text = hit.Excerpt, TextWrapping = TextWrapping.Wrap, MaxLines = 3, Foreground = FluentDesign.Secondary });
                    var open = new Button { Content = card, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    open.Click += async (_, _) => await Safe(async () =>
                    {
                        var requestedRevision = revision;
                        var entry = await store.ReadAsync(access, hit.Id);
                        if (requestedRevision == revision) Edit(entry);
                    });
                    results.Children.Add(open);
                }
                offset = page.NextOffset; more.Visibility = page.HasMore ? Visibility.Visible : Visibility.Collapsed;
                status.Text = offset == 0 ? WorkflowText("Aucune mémoire trouvée.", "No memories found.") : WorkflowText($"{offset} mémoire(s) affichée(s)", $"{offset} memories shown");
            }
            finally
            {
                if (stamp == revision) { searching = null; progress.Visibility = Visibility.Collapsed; more!.IsEnabled = true; }
            }
        }
        async Task ChangeProject(int? preferredChat = null)
        {
            var requested = (projectPicker.SelectedItem as Project)?.Id;
            var stamp = ++revision; searching?.Cancel(); searching = null;
            initializing = true;
            chatPicker.ItemsSource = null; editor.Visibility = Visibility.Collapsed;
            initializing = false;
            progress.Visibility = Visibility.Visible;
            try
            {
                var chats = await ReadStoreAsync(db => db.Chats.AsNoTracking().Where(x => x.ProjectId == requested).OrderByDescending(x => x.Id).ToList());
                if (stamp != revision) return;
                initializing = true;
                chatPicker.ItemsSource = chats; chatPicker.SelectedItem = chats.FirstOrDefault(x => x.Id == preferredChat) ?? chats.FirstOrDefault();
                editor.Visibility = Visibility.Collapsed;
            }
            finally { if (stamp == revision) { initializing = false; progress.Visibility = Visibility.Collapsed; } }
            if (stamp == revision) await Load();
        }
        var initialProject = project?.Id; var initialChat = chat?.Id;
        var view = Action(WorkflowText("Voir la mémoire", "View memory"), () => Safe(async () =>
        {
            browser.Visibility = Visibility.Visible;
            if (projectPicker.ItemsSource == null)
            {
                progress.Visibility = Visibility.Visible;
                var projects = await ReadStoreAsync(db => db.Projects.AsNoTracking().OrderBy(x => x.Name).ToList());
                initializing = true;
                projectPicker.ItemsSource = projects; projectPicker.SelectedItem = projects.FirstOrDefault(x => x.Id == initialProject) ?? projects.FirstOrDefault();
                initializing = false;
                await ChangeProject(initialChat);
            }
            else if (chatPicker.ItemsSource == null) await ChangeProject();
            else await Load();
        }));
        panel.Children.Add(view);
        panel.Children.Add(browser);
        browser.Children.Add(Label(WorkflowText("Les créations, modifications et suppressions ci-dessous sont enregistrées immédiatement dans database.sqlite. Les interrupteurs de skill nécessitent Enregistrer.", "The entries below are saved immediately in database.sqlite. Skill toggles require Save."), 12));
        Grid Pair(FrameworkElement left, FrameworkElement right)
        {
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right);
            return grid;
        }
        browser.Children.Add(Pair(projectPicker, chatPicker));
        browser.Children.Add(Pair(scopePicker, categoryPicker));
        foreach (var control in new FrameworkElement[] { search, progress, status }) browser.Children.Add(control);
        browser.Children.Add(Row(Action(WorkflowText("Actualiser", "Refresh"), () => Safe(() => Load())), Action(WorkflowText("Créer une mémoire", "Create memory"), () => { Edit(null); return Task.CompletedTask; })));
        browser.Children.Add(editor); browser.Children.Add(results);
        more = Action(WorkflowText("Afficher plus", "Show more"), () => Safe(() => Load(true))); more.Visibility = Visibility.Collapsed; browser.Children.Add(more);
        editor.Children.Add(provenance);
        editor.Children.Add(Pair(scopeEdit, categoryEdit));
        foreach (var control in new FrameworkElement[] { key, name, tags, content }) editor.Children.Add(control);
        editor.Children.Add(Row(Action(WorkflowText("Enregistrer la mémoire", "Save memory"), () => Safe(async () =>
        {
            var stamp = revision;
            var saved = await store.SaveAsync(editingAccess ?? Access(), new(scopeEdit.SelectedIndex == 1 ? "shared" : "conversation", categories[categoryEdit.SelectedIndex + 1], key.Text, name.Text, content.Text, tags.Text), selected?.Id, selected?.Version);
            if (stamp == revision) { Edit(saved); await Load(); }
        })), Action(WorkflowText("Supprimer", "Delete"), () => Safe(async () =>
        {
            if (selected == null) return;
            var entry = selected; var access = editingAccess!;
            var stamp = revision;
            var confirmation = new ContentDialog { XamlRoot = panel.XamlRoot, Title = WorkflowText("Supprimer cette mémoire ?", "Delete this memory?"), Content = entry.Title,
                PrimaryButtonText = WorkflowText("Supprimer", "Delete"), CloseButtonText = WorkflowText("Annuler", "Cancel") };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            await store.DeleteAsync(access, entry.Id, entry.Version);
            if (stamp == revision) { selected = null; editor.Visibility = Visibility.Collapsed; await Load(); }
        })), Action(WorkflowText("Fermer", "Close"), () => { editor.Visibility = Visibility.Collapsed; return Task.CompletedTask; })));
        projectPicker.SelectionChanged += async (_, _) => { if (!initializing) await Safe(() => ChangeProject()); };
        chatPicker.SelectionChanged += async (_, _) => { if (!initializing) { editor.Visibility = Visibility.Collapsed; await Safe(() => Load()); } };
        scopePicker.SelectionChanged += async (_, _) => { if (!initializing) await Safe(() => Load()); };
        categoryPicker.SelectionChanged += async (_, _) => { if (!initializing) await Safe(() => Load()); };
        var searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        search.TextChanged += (_, _) => { searchTimer.Stop(); searchTimer.Start(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); if (!initializing) await Safe(() => Load()); };
        panel.Unloaded += (_, _) => { searchTimer.Stop(); searching?.Cancel(); searching = null; revision++; initializing = false; };
        return panel;
    }

    string ScopeLabel(string scope) => scope == "shared" ? WorkflowText("Partagée", "Shared") : WorkflowText("Conversation", "Conversation");
    string CategoryLabel(string category) => category switch { "project" => WorkflowText("Projet", "Project"), "user" => WorkflowText("Utilisateur", "User"), _ => WorkflowText("Général", "General") };
}

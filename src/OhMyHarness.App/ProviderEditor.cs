using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed class ProviderDraft
    {
        public int Id { get; init; }
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Model { get; set; } = "";
        public byte[] ProtectedKey { get; init; } = [];
        public string PendingKey { get; set; } = "";
        public bool DeleteKey { get; set; }
        public int ContextLimit { get; set; } = 128000;
        public bool SupportsImages { get; set; } = true;
        public string Kind { get; set; } = "openai";
        public string CompositeJson { get; set; } = "";
        public string Username { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public bool AutoStart { get; set; }
        public bool OpenCodeTools { get; set; }
        public override string ToString() => string.IsNullOrWhiteSpace(Model) ? Name : $"{Name} · {Model}";
    }

    sealed class ProviderEditorState
    {
        public required StackPanel Panel { get; init; }
        public required List<ProviderDraft> Drafts { get; init; }
        public required TextBlock Error { get; init; }
        public ProviderDraft? Selected { get; set; }
        public Action Commit { get; set; } = () => { };
    }

    ProviderEditorState BuildProviderEditor(int selectedProviderId)
    {
        var drafts = db.Providers.Local.Where(x => db.Entry(x).State != EntityState.Deleted).OrderBy(x => x.Id)
            .Select(x => new ProviderDraft { Id = x.Id, Name = x.Name, BaseUrl = x.BaseUrl, Model = x.Model, ProtectedKey = [.. x.ProtectedKey], ContextLimit = x.ContextLimit, SupportsImages = x.SupportsImages,
                Kind = x.Kind, CompositeJson=x.CompositeJson, Username = x.Username, ExecutablePath = x.ExecutablePath, AutoStart = x.AutoStart, OpenCodeTools = x.OpenCodeTools })
            .ToList();
        var state = new ProviderEditorState { Panel = new StackPanel(), Drafts = drafts, Error = Label("", 12) };
        state.Error.Tag = null;

        var cards = new StackPanel { Spacing = 8 };
        var editor = new StackPanel { Spacing = 12, Visibility = Visibility.Collapsed };
        var back = new Button { Content = WorkflowText("← Fournisseurs", "← Providers") };
        var editorTitle = Label("", 20); editorTitle.Tag = null;
        var chooser = new ComboBox { Header = T("Fournisseurs configurés"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = T("Nom du fournisseur"), MaxLength = 100 };
        var url = new TextBox { Header = T("URL de base de l’API") };
        var key = new PasswordBox { Header = T("Clé API (vide : conserver la clé enregistrée)") };
        var model = new ComboBox { Header = T("Identifiant du modèle"), IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var limit = new NumberBox { Header = T("Fenêtre de contexte du modèle (tokens)"), Minimum = 1024, Maximum = 10_000_000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var vision = new CheckBox { Content = T("Ce modèle accepte les images") };
        var deleteKey = new CheckBox { Content = T("Supprimer la clé enregistrée") };
        var username = new TextBox { Header = T("Utilisateur OpenCode"), PlaceholderText = "opencode" };
        var executable = new TextBox { Header = T("Chemin vers opencode.exe (facultatif)"), PlaceholderText = @"C:\…\OpenCode.exe", HorizontalAlignment = HorizontalAlignment.Stretch };
        var browseExecutable = new Button { Content = T("Parcourir…"), VerticalAlignment = VerticalAlignment.Bottom };
        var execGrid = new Grid();
        execGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        execGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(executable, 0); Grid.SetColumn(browseExecutable, 1);
        browseExecutable.Margin = new Thickness(8, 0, 0, 0);
        execGrid.Children.Add(executable); execGrid.Children.Add(browseExecutable);
        var autoStart = new CheckBox { Content = T("Démarrer automatiquement le serveur OpenCode") };
        var openCodeTools = new CheckBox { Content = T("Activer les outils agent OpenCode avec demande d’autorisation") };
        var info = Label(T("Créez autant de connexions que nécessaire. Chaque instance conserve sa propre clé, son URL, son modèle et sa limite de contexte."), 12); info.Tag = null;
        var testConnection = new Button { Content = T("Tester la connexion") };
        var importModels = new Button { Content = T("Importer les modèles OpenCode") };
        var addOpenAi = new MenuFlyoutItem { Text = T("+ OpenAI compatible") };
        var addDeepSeek = new MenuFlyoutItem { Text = "+ DeepSeek" };
        var addOpenCode = new MenuFlyoutItem { Text = "+ OpenCode" };
        var addComposite = new MenuFlyoutItem { Text = "+ Modèle composé / Composite" };
        var compositePanel=new StackPanel();
        var duplicate = new Button { Content = T("Dupliquer") };
        var remove = new Button { Content = T("Supprimer le fournisseur") };
        bool refreshing = false;

        string NextName(string baseName)
        {
            var index = 1; var candidate = baseName;
            while (drafts.Any(x => string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase))) candidate = baseName + " " + ++index;
            return candidate;
        }

        Provider AsProvider(ProviderDraft draft) => new() { Name = draft.Name, BaseUrl = draft.BaseUrl, Model = draft.Model, ContextLimit = draft.ContextLimit, SupportsImages = draft.SupportsImages,
            Kind = draft.Kind, Username = draft.Username, ExecutablePath = draft.ExecutablePath, AutoStart = draft.AutoStart, OpenCodeTools = draft.OpenCodeTools };
        void Select(ProviderDraft? draft)
        {
            refreshing = true; state.Selected = draft;
            editorTitle.Text = draft?.Name ?? "";
            var enabled = draft != null;
            foreach (var control in new Control[] { name, url, key, model, limit, vision, deleteKey, testConnection, importModels, username, executable, browseExecutable, autoStart, openCodeTools }) control.IsEnabled = enabled;
            remove.IsEnabled = duplicate.IsEnabled = enabled;
            name.Text = draft?.Name ?? ""; url.Text = draft?.BaseUrl ?? ""; model.Text = draft?.Model ?? "";
            model.ItemsSource = draft == null ? null : ModelCatalog.GetModelsForProvider(AsProvider(draft));
            limit.Value = draft?.ContextLimit ?? 128000; vision.IsChecked = draft?.SupportsImages ?? true; deleteKey.IsChecked = draft?.DeleteKey ?? false;
            username.Text = draft?.Username ?? ""; executable.Text = draft?.ExecutablePath ?? ""; autoStart.IsChecked = draft?.AutoStart ?? false;
            openCodeTools.IsChecked = draft?.OpenCodeTools ?? false;
            var openCode = draft?.Kind == "opencode";
            username.Visibility = execGrid.Visibility = autoStart.Visibility = openCodeTools.Visibility = openCode ? Visibility.Visible : Visibility.Collapsed;
            key.Header = T(openCode ? "Mot de passe du serveur (vide : aucun)" : "Clé API (vide : conserver la clé enregistrée)");
            importModels.Content = T(openCode ? "Importer les modèles OpenCode" : "Importer les modèles");
            key.Password = draft?.PendingKey ?? "";
            key.PlaceholderText = draft?.ProtectedKey.Length > 0 && draft.DeleteKey == false ? T("Clé déjà enregistrée") : T("Votre clé API");
            var composite=draft?.Kind=="composite";
            foreach(var control in new UIElement[]{url,key,model,limit,vision,deleteKey,testConnection,importModels})control.Visibility=composite?Visibility.Collapsed:Visibility.Visible;
            compositePanel.Children.Clear();if(composite)compositePanel.Children.Add(BuildCompositeModelEditor(draft!,drafts));
            refreshing = false;
        }
        void Refresh(ProviderDraft? draft)
        {
            refreshing = true; chooser.ItemsSource = null; chooser.ItemsSource = drafts.ToList(); chooser.SelectedItem = draft; refreshing = false; Select(draft); RenderCards(); editor.Visibility = draft == null ? Visibility.Collapsed : Visibility.Visible; cards.Visibility = draft == null ? Visibility.Visible : Visibility.Collapsed;
        }
        state.Commit = () =>
        {
            if (state.Selected == null) return;
            if(state.Selected.Kind=="composite"){state.Selected.Name=name.Text;return;}
            state.Selected.Name = name.Text; state.Selected.BaseUrl = url.Text; state.Selected.Model = model.Text;
            if (double.IsFinite(limit.Value)) state.Selected.ContextLimit = (int)limit.Value;
            state.Selected.SupportsImages = vision.IsChecked == true; state.Selected.DeleteKey = deleteKey.IsChecked == true; state.Selected.PendingKey = key.Password;
            state.Selected.Username = username.Text; state.Selected.ExecutablePath = executable.Text; state.Selected.AutoStart = autoStart.IsChecked == true;
            state.Selected.OpenCodeTools = openCodeTools.IsChecked == true;
        };

        chooser.SelectionChanged += (_, _) => { if (refreshing) return; state.Commit(); Select(chooser.SelectedItem as ProviderDraft); };
        name.TextChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.Name = name.Text; };
        name.LostFocus += (_, _) => { if (!refreshing) { if (state.Selected != null) model.ItemsSource = ModelCatalog.GetModelsForProvider(AsProvider(state.Selected)); Refresh(state.Selected); } };
        url.TextChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.BaseUrl = url.Text; };
        url.LostFocus += (_, _) => { if (!refreshing && state.Selected != null) { model.ItemsSource = ModelCatalog.GetModelsForProvider(AsProvider(state.Selected)); } };
        key.PasswordChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.PendingKey = key.Password; };
        model.TextSubmitted += (_, args) =>
        {
            if (!refreshing && state.Selected != null)
            {
                state.Selected.Model = args.Text;
                if (ModelCatalog.GetDefaultContextLimit(args.Text) is int defaultLimit && state.Selected.ContextLimit <= 128000)
                {
                    state.Selected.ContextLimit = defaultLimit; limit.Value = defaultLimit;
                }
            }
        };
        model.SelectionChanged += (_, _) =>
        {
            if (!refreshing && state.Selected != null && model.SelectedItem is string selected)
            {
                state.Selected.Model = selected;
                if (ModelCatalog.GetDefaultContextLimit(selected) is int defaultLimit && state.Selected.ContextLimit <= 128000)
                {
                    state.Selected.ContextLimit = defaultLimit; limit.Value = defaultLimit;
                }
            }
        };
        model.LostFocus += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.Model = model.Text; };
        limit.ValueChanged += (_, _) => { if (!refreshing && state.Selected != null && double.IsFinite(limit.Value)) state.Selected.ContextLimit = (int)limit.Value; };
        vision.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.SupportsImages = true; };
        vision.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.SupportsImages = false; };
        deleteKey.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.DeleteKey = true; };
        deleteKey.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.DeleteKey = false; };
        username.TextChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.Username = username.Text; };
        executable.TextChanged += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.ExecutablePath = executable.Text; };
        browseExecutable.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(settingsWindow ?? this));
                var file = await picker.PickSingleFileAsync();
                if (file != null && state.Selected != null)
                {
                    executable.Text = file.Path;
                    state.Selected.ExecutablePath = file.Path;
                }
            }
            catch (Exception ex) { info.Text = ex.Message; }
        };
        autoStart.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.AutoStart = true; };
        autoStart.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.AutoStart = false; };
        openCodeTools.Checked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.OpenCodeTools = true; };
        openCodeTools.Unchecked += (_, _) => { if (!refreshing && state.Selected != null) state.Selected.OpenCodeTools = false; };
        void AddPreset(string preset)
        {
            var deepSeek = preset == "deepseek";
            var draft = new ProviderDraft { Name = NextName(deepSeek ? "DeepSeek" : T("OpenAI compatible")), BaseUrl = deepSeek ? "https://api.deepseek.com" : "https://api.openai.com/v1", Model = deepSeek ? "deepseek-chat" : "gpt-4.1-mini", ContextLimit = 128000, SupportsImages = true };
            drafts.Add(draft); Refresh(draft); name.Focus(FocusState.Programmatic); name.SelectAll();
        }
        addOpenAi.Click += (_, _) => AddPreset("openai");
        addComposite.Click+=(_,_)=>{state.Commit();var draft=new ProviderDraft{Name=NextName("Modèle composé"),Kind="composite",CompositeJson=new CompositeModel().Json()};drafts.Add(draft);Refresh(draft);};
        addDeepSeek.Click += (_, _) => AddPreset("deepseek");
        addOpenCode.Click += (_, _) =>
        {
            var defaultDesktop = ResolveOpenCodeExecutable(null);
            var hasDesktop = File.Exists(defaultDesktop);
            var draft = new ProviderDraft
            {
                Name = NextName("OpenCode"),
                Kind = "opencode",
                BaseUrl = "http://127.0.0.1:4096",
                Username = "opencode",
                Model = "opencode/big-pickle",
                ExecutablePath = hasDesktop ? defaultDesktop : "",
                AutoStart = hasDesktop,
                ContextLimit = 200000,
                SupportsImages = true
            };
            drafts.Add(draft); Refresh(draft); name.Focus(FocusState.Programmatic); name.SelectAll();
        };
        duplicate.Click += (_, _) =>
        {
            if (state.Selected == null) return;
            state.Commit();
            var source = state.Selected;
            var draft = new ProviderDraft { Name = NextName(source.Name + " " + T("copie")), BaseUrl = source.BaseUrl, Model = source.Model, ContextLimit = source.ContextLimit, SupportsImages = source.SupportsImages,
                Kind = source.Kind, CompositeJson=source.CompositeJson, Username = source.Username, ExecutablePath = source.ExecutablePath, AutoStart = source.AutoStart, OpenCodeTools = source.OpenCodeTools };
            drafts.Add(draft); Refresh(draft); name.Focus(FocusState.Programmatic); name.SelectAll();
        };
        remove.Click += (_, _) =>
        {
            if (state.Selected == null) return;
            var index = drafts.IndexOf(state.Selected); drafts.Remove(state.Selected);
            Refresh(drafts.Count == 0 ? null : drafts[Math.Clamp(index, 0, drafts.Count - 1)]);
        };
        testConnection.Click += async (_, _) =>
        {
            if (state.Selected == null) return;
            testConnection.IsEnabled = false;
            info.Text = T("Test de connexion en cours…");
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var secret = state.Selected.PendingKey.Length > 0 ? state.Selected.PendingKey : state.Selected.DeleteKey ? "" : KeyVault.Decrypt(state.Selected.ProtectedKey);
                if (state.Selected.Kind == "opencode")
                {
                    var target = AsProvider(state.Selected);
                    await EnsureOpenCodeServerAsync(target, secret, timeout.Token);
                    var version = await openCodeEngine.HealthAsync(target, secret, timeout.Token);
                    info.Text = string.Format(T("Connexion réussie · Serveur OpenCode v{0} opérationnel."), version);
                }
                else
                {
                    var list = await engine.ModelsAsync(new Provider { BaseUrl = state.Selected.BaseUrl.Trim() }, secret, timeout.Token);
                    info.Text = this.state.Language == "en" ? $"Connection successful · {list.Count} models reachable." : $"Connexion réussie · {list.Count} modèles accessibles.";
                }
            }
            catch (Exception ex) { info.Text = ex.Message; }
            finally { testConnection.IsEnabled = true; }
        };
        importModels.Click += async (_, _) =>
        {
            if (state.Selected == null) return;
            importModels.IsEnabled = false;
            info.Text = T("Importation des modèles…");
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                var secret = state.Selected.PendingKey.Length > 0 ? state.Selected.PendingKey : state.Selected.DeleteKey ? "" : KeyVault.Decrypt(state.Selected.ProtectedKey);
                List<string> list;
                if (state.Selected.Kind == "opencode")
                {
                    var target = AsProvider(state.Selected);
                    await EnsureOpenCodeServerAsync(target, secret, timeout.Token);
                    var discovered = await openCodeEngine.ModelsAsync(target, secret, project?.GetSourceFolders().FirstOrDefault(), timeout.Token);
                    list = discovered.Select(x => x.Reference).ToList();
                    var selectedModel = discovered.FirstOrDefault(x => x.Reference == state.Selected.Model) ?? discovered.FirstOrDefault();
                    if (selectedModel != null)
                    {
                        if (string.IsNullOrWhiteSpace(state.Selected.Model) || !discovered.Any(x => x.Reference == state.Selected.Model))
                        {
                            state.Selected.Model = selectedModel.Reference;
                        }
                        if (selectedModel.ContextLimit is int context) { state.Selected.ContextLimit = context; limit.Value = context; }
                        state.Selected.SupportsImages = selectedModel.SupportsImages; vision.IsChecked = selectedModel.SupportsImages;
                    }
                }
                else list = await engine.ModelsAsync(new Provider { BaseUrl = state.Selected.BaseUrl.Trim() }, secret, timeout.Token);
                var selected = state.Selected.Model; model.ItemsSource = list; model.Text = selected;
                info.Text = string.Format(T("Connexion réussie · {0} modèles importés."), list.Count);
            }
            catch (Exception ex) { info.Text = ex.Message; }
            finally { importModels.IsEnabled = true; }
        };

        void RenderCards()
        {
            cards.Children.Clear();
            foreach (var draft in drafts)
            {
                var gear = new Button { Width = 34, Height = 34, Padding = new(0) };
                FluentDesign.IconButton(gear, "\uE713", WorkflowText("Réglages de ", "Settings for ") + draft.Name, false);
                gear.Click += (_, _) => { state.Commit(); Refresh(draft); };
                cards.Children.Add(FluentDesign.Setting(draft.Name, draft.Kind == "composite" ? WorkflowText("Modèle composé", "Composite model") : draft.Model + " · " + draft.Kind, gear));
            }
            if (drafts.Count == 0) cards.Children.Add(Label(WorkflowText("Ajoutez un fournisseur pour commencer.", "Add a provider to get started.")));
        }
        back.Click += (_, _) => { state.Commit(); RenderCards(); editor.Visibility = Visibility.Collapsed; cards.Visibility = Visibility.Visible; };
        var add = new DropDownButton { Content = WorkflowText("＋ Ajouter un fournisseur", "＋ Add provider") };
        var presets = new MenuFlyout(); add.Flyout = presets;
        foreach (var item in new[] { addOpenAi, addDeepSeek, addOpenCode, addComposite }) presets.Items.Add(item);
        state.Panel.Spacing = 12;
        state.Panel.Children.Add(add); state.Panel.Children.Add(cards); state.Panel.Children.Add(editor); state.Panel.Children.Add(state.Error);
        foreach (var item in new UIElement[] { back, editorTitle, info, name, compositePanel, url, username, key, execGrid, autoStart, openCodeTools, model, Row(testConnection, importModels), limit, vision, deleteKey, Row(duplicate, remove) }) editor.Children.Add(item);
        Refresh(drafts.FirstOrDefault(x => x.Id == selectedProviderId) ?? drafts.FirstOrDefault());
        editor.Visibility = Visibility.Collapsed; cards.Visibility = Visibility.Visible;
        return state;
    }

    string? ValidateProviderDrafts(ProviderEditorState editor)
    {
        editor.Commit();
        if(db.PendingInputs.AsNoTracking().Select(x=>x.ProviderId).ToList().Any(id=>!editor.Drafts.Any(draft=>draft.Id==id)))return "Un fournisseur est utilisé par un message en attente. / Provider used by a queued message.";
        if (editor.Drafts.Any(x => string.IsNullOrWhiteSpace(x.Name))) return T("Le nom du fournisseur est requis.");
        foreach (var draft in editor.Drafts)
        {
            if(draft.Kind=="composite")
            {
                try{CompositeModel.Read(draft.CompositeJson).Validate(editor.Drafts.Where(x=>x.Id>0).Select(x=>new Provider{Id=x.Id,Kind=x.Kind}));}
                catch(Exception ex){return draft.Name+" : "+ex.Message;}continue;
            }
            try { ChatEngine.Endpoint(draft.BaseUrl.Trim(), draft.Kind == "opencode" ? "global/health" : "chat/completions"); }
            catch { return T("URL HTTP(S) invalide.") + " " + draft.Name; }
            if (string.IsNullOrWhiteSpace(draft.Model) || draft.ContextLimit < 1024) return T("Modèle et limite de contexte requis.") + " " + draft.Name;
        }
        return null;
    }

    async Task<Provider?> SaveProviderDraftsAsync(ProviderEditorState editor)
    {
        var entities = new Dictionary<ProviderDraft, Provider>();
        foreach (var existing in db.Providers.Local.ToList())
            if (!editor.Drafts.Any(x => x.Id == existing.Id)) db.Providers.Remove(existing);
        foreach (var draft in editor.Drafts)
        {
            var entity = draft.Id == 0 ? null : db.Providers.Local.FirstOrDefault(x => x.Id == draft.Id);
            if (entity == null) { entity = new Provider(); db.Providers.Add(entity); }
            entity.Name = draft.Name.Trim(); entity.BaseUrl = draft.BaseUrl.Trim().TrimEnd('/'); entity.Model = draft.Model.Trim();
            entity.ContextLimit = draft.ContextLimit; entity.SupportsImages = draft.SupportsImages;
            entity.Kind = draft.Kind; entity.Username = draft.Username.Trim(); entity.ExecutablePath = draft.ExecutablePath.Trim(); entity.AutoStart = draft.AutoStart;
            entity.OpenCodeTools = draft.OpenCodeTools;
            entity.CompositeJson=draft.CompositeJson;
            if(entity.IsComposite)
            {
                var config=CompositeModel.Read(entity.CompositeJson);var target=CompositeModel.Resolve(config.Orchestrator,db.Providers.Local);
                entity.Model=target.Model;entity.ContextLimit=target.ContextLimit;entity.SupportsImages=target.SupportsImages;entity.BaseUrl="";entity.ProtectedKey=[];
            }
            if (draft.DeleteKey) entity.ProtectedKey = [];
            else if (!string.IsNullOrWhiteSpace(draft.PendingKey)) entity.ProtectedKey = KeyVault.Encrypt(draft.PendingKey.Trim());
            entities[draft] = entity;
        }
        await db.SaveChangesAsync();
        if (editor.Selected != null && entities.TryGetValue(editor.Selected, out var selected)) return selected;
        return editor.Drafts.Count == 0 ? null : entities[editor.Drafts[0]];
    }
}

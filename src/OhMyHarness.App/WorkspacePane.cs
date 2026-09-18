using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using OhMyHarness.Core;
using System.Text.Json.Nodes;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    Grid mainArea = null!;
    readonly Pivot toolTabs = new();
    readonly TextBox terminalOutput = OutputBox();
    readonly TextBox terminalCommand = new() { PlaceholderText = "PowerShell…", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 140 };
    readonly TextBlock terminalDirectory = Label("", 12);
    readonly TextBox gitOutput = OutputBox();
    readonly ListView fileList = new() { IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single };
    readonly TextBlock fileLocation = Label("", 12);
    readonly TextBox fileContent = OutputBox();
    readonly Grid filePanel = new();
    readonly SemaphoreSlim approvalQueue = new(1, 1);
    CancellationTokenSource? terminalRun;
    string? fileDirectory, previewFolder, previewHost;
    string? selectedFile;
    bool toolsMaximized;
    bool activatingTool;
    int fileRevision;
    static TextBox OutputBox() => new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

    UIElement BuildComposer()
    {
        var overlay = new Grid();
        composer.MinHeight = 124; composer.MaxHeight = 260; composer.Padding = new Thickness(14, 12, 14, 58);
        overlay.Children.Add(composer);
        var plus = new Button { Content = "+", FontSize = 24, Width = 38, Height = 38, Padding = new(0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(10, 0, 0, 10) };
        idleOnly.Add(plus);
        var menu = new MenuFlyout(); plus.Flyout = menu;
        menu.Opening += (_, _) => BuildComposerMenu(menu);
        overlay.Children.Add(plus);
        send.Content = "↑"; send.Width = 38; send.Height = 38; send.Padding = new(0); send.FontSize = 22;
        stop.Content = "■"; stop.Width = 38; stop.Height = 38; stop.Padding = new(0);
        var right = Row(stop, send); right.HorizontalAlignment = HorizontalAlignment.Right; right.VerticalAlignment = VerticalAlignment.Bottom; right.Margin = new(0, 0, 10, 10);
        overlay.Children.Add(right);
        return overlay;
    }
    void BuildComposerMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        MenuFlyoutItem Item(string label, Func<Task> action)
        {
            var item = new MenuFlyoutItem { Text = T(label) };
            item.Click += async (_, _) => await Guard(action);
            return item;
        }
        menu.Items.Add(Item("Joindre des images", AttachImages));
        menu.Items.Add(Item("Associer un dossier source", AttachFolder));
        menu.Items.Add(Item("Effacer PJ", () => { pendingImages.Clear(); UpdateAttachments(); return Task.CompletedTask; }));
        menu.Items.Add(new MenuFlyoutSeparator());
        var skillMenu = new MenuFlyoutSubItem { Text = "Skills" };
        foreach (var skill in Skills.All)
        {
            var toggle = new ToggleMenuFlyoutItem { Text = state.Language == "en" ? skill.EnglishName : skill.FrenchName, IsChecked = Skills.Enabled(state.EnabledSkills, skill.Id) };
            toggle.Click += async (_, _) => await Guard(async () =>
            {
                if (generation != null) return;
                var enabled = state.EnabledSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                if (toggle.IsChecked) enabled.Add(skill.Id); else enabled.Remove(skill.Id);
                state.EnabledSkills = string.Join(',', enabled); await db.SaveChangesAsync();
            });
            skillMenu.Items.Add(toggle);
        }
        menu.Items.Add(skillMenu);
        var templates = new MenuFlyoutSubItem { Text = "Templates" };
        foreach (var template in db.Templates.Local.Where(x => db.Entry(x).State != EntityState.Deleted).OrderBy(x => x.Name))
        {
            var captured = template;
            templates.Items.Add(Item(template.Name, async () =>
            {
                if (!string.IsNullOrWhiteSpace(composer.Text))
                {
                    var confirm = new ContentDialog { XamlRoot = root.XamlRoot, Title = T("Remplacer le brouillon ?"), Content = T("Le template remplacera le texte actuel. Les pièces jointes sont conservées."), PrimaryButtonText = T("Remplacer"), CloseButtonText = T("Annuler") };
                    if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                }
                composer.Text = captured.Content; composer.Focus(FocusState.Programmatic); composer.Select(composer.Text.Length, 0);
            }));
        }
        templates.Items.Add(Item("Modifier les templates…", Settings));
        menu.Items.Add(templates);
        menu.Items.Add(Item("Réglages", Settings));
    }

    void BuildToolsPane()
    {
        var container = new Grid { Padding = new(10), RowSpacing = 8 };
        container.RowDefinitions.Add(new() { Height = GridLength.Auto }); container.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var toolbar = new Grid(); toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        toolbar.Children.Add(Label("Outils", 16));
        var actions = Row(Action("⛶", () => { toolsMaximized = !toolsMaximized; ResizeLayout(); return Task.CompletedTask; }), Action("×", ToggleBrowser));
        ToolTipService.SetToolTip(actions.Children[0], T("Agrandir / restaurer le panneau"));
        Grid.SetColumn(actions, 1); toolbar.Children.Add(actions); container.Children.Add(toolbar);
        var web = new Grid { RowSpacing = 8 };
        web.RowDefinitions.Add(new() { Height = GridLength.Auto }); web.RowDefinitions.Add(new() { Height = GridLength.Auto }); web.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        web.Children.Add(browserAccess);
        var nav = new Grid { ColumnSpacing = 4 }; nav.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); nav.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); nav.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        nav.Children.Add(address);
        var go = Action("→", async () => { await NavigateAsync(address.Text, CancellationToken.None); }); Grid.SetColumn(go, 1); nav.Children.Add(go);
        var local = Action("📂", PickPreviewAsync, true); Grid.SetColumn(local, 2); nav.Children.Add(local); ToolTipService.SetToolTip(local, T("Ouvrir un fichier local"));
        address.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await Guard(async () => { await NavigateAsync(address.Text, CancellationToken.None); }); } };
        Grid.SetRow(nav, 1); web.Children.Add(nav); Grid.SetRow(browser, 2); web.Children.Add(browser);
        idleOnly.Add(browserAccess); idleOnly.Add(address); idleOnly.Add(go);
        toolTabs.Items.Add(new PivotItem { Header = "Web", Content = web });

        var terminal = new Grid { RowSpacing = 8 };
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) terminal.RowDefinitions.Add(new() { Height = height });
        terminal.Children.Add(terminalDirectory); Grid.SetRow(terminalOutput, 1); terminal.Children.Add(terminalOutput); Grid.SetRow(terminalCommand, 2); terminal.Children.Add(terminalCommand);
        var run = Action("Exécuter", async () => { await RunTerminalAsync(terminalCommand.Text, false, CancellationToken.None); }, true);
        var cancel = Action("Arrêter", () => { terminalRun?.Cancel(); return Task.CompletedTask; });
        var terminalActions = Row(run, cancel); Grid.SetRow(terminalActions, 3); terminal.Children.Add(terminalActions);
        idleOnly.Add(terminalCommand);
        toolTabs.Items.Add(new PivotItem { Header = "Terminal", Content = terminal });

        var git = new Grid { RowSpacing = 8 }; git.RowDefinitions.Add(new() { Height = GridLength.Auto }); git.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        git.Children.Add(Action("Actualiser les changements", async () => { await RefreshGitAsync(CancellationToken.None); }));
        Grid.SetRow(gitOutput, 1); git.Children.Add(gitOutput); toolTabs.Items.Add(new PivotItem { Header = "Git", Content = git });

        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star) }) filePanel.RowDefinitions.Add(new() { Height = height });
        filePanel.RowSpacing = 8;
        filePanel.Children.Add(Row(Action("↑", async () =>
        {
            if (fileDirectory == null || string.IsNullOrEmpty(project?.SourceFolder)) return;
            var parent = Directory.GetParent(fileDirectory)?.FullName;
            if (parent != null && Path.GetRelativePath(project.SourceFolder, parent) is var relative && !relative.StartsWith("..")) await LoadFilesAsync(parent);
        }), Action("Actualiser", () => LoadFilesAsync(fileDirectory ?? project?.SourceFolder)), Action("Ouvrir dans Web", async () => { if (selectedFile != null) await OpenLocalPreviewAsync(selectedFile, CancellationToken.None); })));
        Grid.SetRow(fileLocation, 1); filePanel.Children.Add(fileLocation);
        Grid.SetRow(fileList, 2); filePanel.Children.Add(fileList); Grid.SetRow(fileContent, 3); filePanel.Children.Add(fileContent);
        fileList.ItemClick += async (_, e) => await Guard(async () =>
        {
            if (e.ClickedItem is not FileEntry file) return;
            if (file.Directory) await LoadFilesAsync(file.Path);
            else
            {
                selectedFile = file.Path;
                try { fileContent.Text = await new SourceAccess(project!.SourceFolder).ReadAsync(Path.GetRelativePath(project.SourceFolder, file.Path), CancellationToken.None); }
                catch (Exception ex) { fileContent.Text = T("Aperçu texte indisponible. Utilisez Ouvrir dans Web.") + "\n" + ex.Message; }
            }
        });
        toolTabs.Items.Add(new PivotItem { Header = T("Fichiers"), Content = filePanel });
        toolTabs.SelectionChanged += async (_, _) => { if (browserVisible) await Guard(ActivateToolAsync); };
        Grid.SetRow(toolTabs, 1); container.Children.Add(toolTabs);
        browserPanel.Child = container; Grid.SetColumn(browserPanel, 1); workspace.Children.Add(browserPanel);
    }
    void RefreshToolLanguage()
    {
        if (toolTabs.Items.Count == 4) ((PivotItem)toolTabs.Items[3]).Header = T("Fichiers");
    }
    async Task ActivateToolAsync()
    {
        if (activatingTool) return;
        activatingTool = true;
        try
        {
            switch (toolTabs.SelectedIndex)
            {
                case 0: await EnsureBrowser(); break;
                case 1: terminalDirectory.Text = T("Dossier : ") + (project?.SourceFolder ?? "") + "\n" + T("Chaque commande démarre une session PowerShell indépendante (60 s max)."); break;
                case 2: await RefreshGitAsync(CancellationToken.None); break;
                case 3: await LoadFilesAsync(fileDirectory ?? project?.SourceFolder); break;
            }
        }
        finally { activatingTool = false; }
    }
    async Task ShowToolAsync(int index)
    {
        browserVisible = true; browserPanel.Visibility = Visibility.Visible;
        activatingTool = true; toolTabs.SelectedIndex = index; activatingTool = false;
        ResizeLayout(); await ActivateToolAsync();
    }
    void ResetWorkspaceTools()
    {
        terminalRun?.Cancel(); fileRevision++;
        fileDirectory = null; selectedFile = null; previewFolder = null; previewHost = null;
        fileList.ItemsSource = null; fileContent.Text = ""; gitOutput.Text = "";
        terminalOutput.Text = ""; terminalDirectory.Text = project?.SourceFolder ?? "";
        if (browserReady && browser.CoreWebView2.Source.Contains(".preview.invalid")) browser.CoreWebView2.Navigate("about:blank");
    }
    string RequireDirectory()
    {
        if (string.IsNullOrEmpty(project?.SourceFolder) || !Directory.Exists(project.SourceFolder)) throw new InvalidOperationException(T("Associez un dossier source via le bouton +."));
        return LocalPreview.ValidatePath(project.SourceFolder);
    }
    record FileEntry(string Path, bool Directory)
    {
        public override string ToString() => (Directory ? "📁 " : "📄 ") + System.IO.Path.GetFileName(Path);
    }
    async Task LoadFilesAsync(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) { fileLocation.Text = T("Associez un dossier source via le bouton +."); return; }
        var rootFolder = RequireDirectory();
        var path = new SourceAccess(rootFolder).Resolve(Path.GetRelativePath(rootFolder, directory));
        var revision = ++fileRevision;
        var entries = await Task.Run(() => Directory.EnumerateFileSystemEntries(path).Take(1000).Where(p =>
        {
            try { new SourceAccess(rootFolder).Resolve(Path.GetRelativePath(rootFolder, p)); return true; } catch (UnauthorizedAccessException) { return false; }
        }).Select(p => new FileEntry(p, Directory.Exists(p))).OrderByDescending(p => p.Directory).ThenBy(p => p.Path).ToList());
        if (revision != fileRevision) return;
        fileDirectory = path; fileLocation.Text = path; fileList.ItemsSource = entries; selectedFile = null; fileContent.Text = "";
    }
    async Task<string> RefreshGitAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(project?.SourceFolder)) return gitOutput.Text = T("Associez un dossier source via le bouton +.");
        var directory = RequireDirectory();
        var result = "STATUS\n" + await WorkspaceTools.GitAsync(directory, ["--no-pager", "status", "--short", "--branch", "--untracked-files=normal"], ct);
        result += "\nUNSTAGED\n" + await WorkspaceTools.GitAsync(directory, ["--no-pager", "diff", "--no-ext-diff", "--no-textconv", "--", "."], ct);
        result += "\nSTAGED\n" + await WorkspaceTools.GitAsync(directory, ["--no-pager", "diff", "--cached", "--no-ext-diff", "--no-textconv", "--", "."], ct);
        if (project?.SourceFolder == directory) gitOutput.Text = result;
        return result;
    }
    async Task<string> RunTerminalAsync(string command, bool fromAi, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException(T("Commande requise."));
        if (terminalRun != null) return T("Une commande est déjà en cours.");
        var directory = RequireDirectory();
        if (fromAi && !await RequestAccessAsync(T("Exécuter une commande terminal"), directory + "\n\n" + command + "\n\n" + T("Cette commande peut modifier des fichiers et accéder au réseau avec les droits de votre compte Windows."), ct)) return T("Accès refusé par l’utilisateur.");
        ct.ThrowIfCancellationRequested();
        using var running = CancellationTokenSource.CreateLinkedTokenSource(ct); terminalRun = running;
        try
        {
            await ShowToolAsync(1);
            terminalDirectory.Text = directory;
            terminalOutput.Text = "> " + command + "\n" + T("Exécution en cours…");
            var result = await WorkspaceTools.PowerShellAsync(command, directory, running.Token);
            terminalOutput.Text = "> " + command + "\n" + result;
            return result;
        }
        catch (OperationCanceledException) { terminalOutput.Text += "\n" + T("Commande arrêtée ou délai de 60 secondes atteint."); throw; }
        finally { terminalRun = null; }
    }
    async Task<bool> RequestAccessAsync(string action, string details, CancellationToken ct)
    {
        await approvalQueue.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = T("Autorisation supplémentaire"),
                Content = new ScrollViewer { MaxHeight = 400, Content = new TextBlock { Text = action + "\n\n" + details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
                PrimaryButtonText = T("Autoriser une fois"), CloseButtonText = T("Refuser"), DefaultButton = ContentDialogButton.Close };
            using var registration = ct.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
            var response = await dialog.ShowAsync();
            ct.ThrowIfCancellationRequested();
            return response == ContentDialogResult.Primary;
        }
        finally { approvalQueue.Release(); }
    }
    string ResolveRequestedLocalPath(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) throw new ArgumentException(T("Chemin requis."));
        if (Uri.TryCreate(requested, UriKind.Absolute, out var uri) && uri.IsFile) requested = uri.LocalPath;
        return LocalPreview.ValidatePath(Path.IsPathFullyQualified(requested) ? requested : Path.Combine(RequireDirectory(), requested));
    }
    async Task<string> ReadWithApprovalAsync(string requested, CancellationToken ct)
    {
        var path = ResolveRequestedLocalPath(requested);
        new SourceAccess(Path.GetDirectoryName(path)!).Resolve(Path.GetFileName(path));
        if (!await RequestAccessAsync(T("Lire un fichier hors du périmètre du projet"), path + "\n\n" + T("Le contenu sera transmis au fournisseur IA pour cette demande."), ct)) return T("Accès refusé par l’utilisateur.");
        return await new SourceAccess(Path.GetDirectoryName(path)!).ReadAsync(Path.GetFileName(path), ct);
    }
    async Task<string> WriteWithApprovalAsync(string requested, string content, string? oldText, CancellationToken ct)
    {
        var path = ResolveRequestedLocalPath(requested);
        var folder = Path.GetDirectoryName(path)!;
        var access = new SourceAccess(folder);
        access.Resolve(Path.GetFileName(path));
        var detail = path + "\n\n" + (oldText == null ? T("Créer ou remplacer le contenu intégral de ce fichier :") : T("Remplacer le texte suivant :") + "\n" + oldText + "\n\n→") + "\n" + content;
        if (!await RequestAccessAsync(T("Écrire hors du périmètre du projet"), detail, ct)) return T("Accès refusé par l’utilisateur.");
        return oldText == null ? await access.WriteAsync(Path.GetFileName(path), content, ct) : await access.ModifyAsync(Path.GetFileName(path), oldText, content, ct);
    }
    async Task PickPreviewAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*"); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync(); if (file != null) await OpenLocalPreviewAsync(file.Path, CancellationToken.None);
    }
    async Task<string> OpenLocalPreviewAsync(string requested, CancellationToken ct)
    {
        var path = ResolveRequestedLocalPath(requested);
        var folder = Path.GetDirectoryName(path)!;
        LocalPreview.ResolveResource(folder, Uri.EscapeDataString(Path.GetFileName(path)));
        if (!await RequestAccessAsync(T("Ouvrir un fichier local dans le navigateur"), path + "\n\n" + T("Dossier de ressources autorisé : ") + folder + "\n\n" + T("Le fichier et ses ressources locales pourront être lus par la page. Son JavaScript pourra s’exécuter et accéder au réseau. L’IA pourra lire la page et transmettre son contenu au fournisseur. Autorisation valable jusqu’au changement de projet ou à la prochaine ouverture locale."), ct)) return T("Accès refusé par l’utilisateur.");
        await ShowToolAsync(0);
        previewFolder = folder; previewHost = Guid.NewGuid().ToString("N") + ".preview.invalid";
        return await NavigateCoreAsync(new Uri("https://" + previewHost + "/" + Uri.EscapeDataString(Path.GetFileName(path))), ct);
    }
    void ConfigureLocalPreview()
    {
        browser.CoreWebView2.AddWebResourceRequestedFilter("https://*.preview.invalid/*", CoreWebView2WebResourceContext.All);
        browser.CoreWebView2.FrameNavigationStarting += (_, e) => { if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.IsFile) e.Cancel = true; };
        browser.CoreWebView2.WebResourceRequested += async (_, e) =>
        {
            using var deferral = e.GetDeferral();
            try
            {
                var uri = new Uri(e.Request.Uri);
                if (previewFolder == null || uri.Host != previewHost || e.Request.Method != "GET") throw new UnauthorizedAccessException();
                var path = LocalPreview.ResolveResource(previewFolder, uri.AbsolutePath);
                var bytes = await File.ReadAllBytesAsync(path);
                var content = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(content)) { writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); }
                content.Seek(0);
                e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(content, 200, "OK", "Content-Type: " + LocalPreview.Mime(path) + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
            }
            catch { e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Access denied", "Cache-Control: no-store"); }
            finally { deferral.Complete(); }
        };
    }
    void AddWorkspaceToolDefinitions(JsonArray definitions)
    {
        void Add(string name, string description, string? argument)
        {
            var properties = new JsonObject(); var required = new JsonArray();
            if (argument != null) { properties[argument] = new JsonObject { ["type"] = "string" }; required.Add(argument); }
            definitions.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false } } });
        }
        if (Skills.Enabled(state.EnabledSkills, "web")) Add("open_local_file", "Requests user approval, then previews a local file and reads its page. Use a project-relative or absolute Windows path. Never bypass a refusal.", "path");
        if (Skills.Enabled(state.EnabledSkills, "terminal")) Add("run_terminal", "Requests user approval before executing a PowerShell command in the attached project folder. Each invocation is a new session, 60 second timeout. The command runs with the user's Windows privileges.", "command");
        if (Skills.Enabled(state.EnabledSkills, "sources") && !string.IsNullOrEmpty(project?.SourceFolder)) Add("git_changes", "Returns Git status and staged/unstaged diffs from the attached project. Read-only.", null);
    }
}

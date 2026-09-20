using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using System.Text.Json;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed record McpEditorState(StackPanel Panel, Func<bool> Validate, Action Save);
    sealed class McpDraft(McpServer server)
    {
        public McpServer Server { get; } = server;
        public string Secrets { get; set; } = "";
        public bool ClearSecrets { get; set; }
        public override string ToString() => Server.Name;
    }
    McpEditorState BuildMcpEditor()
    {
        var drafts = db.McpServers.Local.Where(x => db.Entry(x).State != EntityState.Deleted)
            .Select(x => new McpDraft(JsonSerializer.Deserialize<McpServer>(JsonSerializer.Serialize(x))!)).ToList();
        var panel = new StackPanel { Spacing = 12 };
        var chooser = new ComboBox { Header = T("Serveur MCP"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = T("Nom"), MaxLength = 100 };
        var enabled = new ToggleSwitch { Header = T("Activé"), OnContent = T("Activé"), OffContent = T("Désactivé") };
        var transport = new ComboBox { Header = "Transport", ItemsSource = new[] { "stdio", "http", "sse" }, SelectedIndex = 0 };
        var command = new TextBox { Header = T("Commande / exécutable"), PlaceholderText = "npx / uvx / node / …" };
        var arguments = new TextBox { Header = T("Arguments (tableau JSON)"), PlaceholderText = "[\"-y\",\"@modelcontextprotocol/server-filesystem\",\"C:\\\\Sources\"]", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 65 };
        var directory = new TextBox { Header = T("Dossier de travail (facultatif)") };
        var url = new TextBox { Header = "URL MCP", PlaceholderText = "https://…/mcp" };
        var secrets = new PasswordBox { Header = T("Secrets JSON (vide : conserver)"), PlaceholderText = "{\"environment\":{…},\"headers\":{…}}" };
        var clear = new CheckBox { Content = T("Effacer les secrets enregistrés") };
        var info = Label("", 12); info.Tag = null;
        var fields = new StackPanel { Spacing = 12 };
        foreach (var control in new UIElement[] { name, enabled, transport, command, arguments, directory, url, secrets, clear }) fields.Children.Add(control);
        McpDraft? selected = null; bool refreshing = false;
        void Capture()
        {
            if (refreshing || selected == null) return;
            var s = selected.Server;
            s.Name = name.Text.Trim(); s.Enabled = enabled.IsOn; s.Transport = transport.SelectedItem as string ?? "stdio";
            s.Command = command.Text.Trim(); s.ArgumentsJson = arguments.Text; s.WorkingDirectory = directory.Text.Trim(); s.Url = url.Text.Trim();
            selected.Secrets = secrets.Password; selected.ClearSecrets = clear.IsChecked == true;
        }
        void VisibilityForTransport()
        {
            bool local = transport.SelectedItem as string == "stdio";
            command.Visibility = arguments.Visibility = directory.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
            url.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        }
        void Select(McpDraft? item)
        {
            refreshing = true; selected = item;
            foreach (var control in fields.Children.OfType<Control>()) control.IsEnabled = item != null;
            name.Text = item?.Server.Name ?? ""; enabled.IsOn = item?.Server.Enabled ?? false;
            transport.SelectedItem = item?.Server.Transport ?? "stdio"; command.Text = item?.Server.Command ?? "";
            arguments.Text = item?.Server.ArgumentsJson ?? "[]"; directory.Text = item?.Server.WorkingDirectory ?? ""; url.Text = item?.Server.Url ?? "";
            secrets.Password = item?.Secrets ?? ""; clear.IsChecked = item?.ClearSecrets ?? false;
            refreshing = false; VisibilityForTransport(); info.Text = "";
        }
        void Refresh(McpDraft? item) { refreshing = true; chooser.ItemsSource = null; chooser.ItemsSource = drafts.ToList(); chooser.SelectedItem = item; refreshing = false; Select(item); }
        chooser.SelectionChanged += (_, _) => { if (refreshing) return; Capture(); Select(chooser.SelectedItem as McpDraft); };
        transport.SelectionChanged += (_, _) => VisibilityForTransport();
        var add = new Button { Content = T("Ajouter un serveur MCP") };
        add.Click += (_, _) => { Capture(); var draft = new McpDraft(new McpServer()); drafts.Add(draft); Refresh(draft); name.Focus(FocusState.Programmatic); name.SelectAll(); };
        var remove = new Button { Content = T("Supprimer") };
        remove.Click += (_, _) => { if (selected == null) return; drafts.Remove(selected); Refresh(drafts.FirstOrDefault()); };
        var test = new Button { Content = T("Tester la connexion MCP") };
        test.Click += async (_, _) =>
        {
            Capture(); if (selected == null) return;
            var draft = selected;
            test.IsEnabled = false;
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            var owner = settingsWindow;
            void CloseTest(object sender, WindowEventArgs args) => cancel.Cancel();
            if (owner != null) owner.Closed += CloseTest;
            try
            {
                draft.Server.Validate();
                var secret = draft.Secrets.Length > 0 ? draft.Secrets : draft.ClearSecrets ? "" : KeyVault.Decrypt(draft.Server.ProtectedSecrets);
                var config = McpSecrets.Parse(secret);
                if (!await RequestAccessAsync("mcp-test|" + draft.Server.Fingerprint(), "MCP · " + draft.Server.Name,
                    draft.Server.ConnectionDetails, draft.Server.Name, cancel.Token)) { info.Text = T("Accès refusé par l’utilisateur."); return; }
                await using var client = await McpSession.ConnectAsync(draft.Server, config, cancel.Token);
                var tools = await client.ListToolsAsync(cancellationToken: cancel.Token);
                info.Text = T("Connexion MCP réussie : ") + tools.Count + " tools\n" + string.Join("\n", tools.Select(x => x.Name));
            }
            catch (Exception ex) { info.Text = T("Connexion MCP impossible. Vérifiez la configuration. ") + ex.GetType().Name; }
            finally { if (owner != null) owner.Closed -= CloseTest; test.IsEnabled = true; }
        };
        bool Validate()
        {
            Capture();
            foreach (var draft in drafts)
                try { draft.Server.Validate(); if (draft.Secrets.Length > 0) _ = McpSecrets.Parse(draft.Secrets); }
                catch (Exception ex) { Refresh(draft); info.Text = ex is JsonException ? T("JSON MCP invalide.") : ex.Message; return false; }
            return true;
        }
        void Save()
        {
            Capture();
            foreach (var old in db.McpServers.Local.ToList()) if (!drafts.Any(x => x.Server.Id == old.Id)) db.McpServers.Remove(old);
            foreach (var draft in drafts)
            {
                var source = draft.Server;
                var target = source.Id == 0 ? new McpServer() : db.McpServers.Local.Single(x => x.Id == source.Id);
                var protectedSecrets = draft.Secrets.Length > 0 ? KeyVault.Encrypt(draft.Secrets) : draft.ClearSecrets ? [] : source.ProtectedSecrets;
                target.Name = source.Name; target.Enabled = source.Enabled; target.Transport = source.Transport; target.Command = source.Command;
                target.ArgumentsJson = source.ArgumentsJson; target.WorkingDirectory = source.WorkingDirectory; target.Url = source.Url; target.ProtectedSecrets = protectedSecrets;
                if (source.Id == 0) db.McpServers.Add(target);
            }
        }
        panel.Children.Add(Label(T("MCP ajoute les outils des serveurs activés. Enregistrer applique la configuration. Les nouvelles connexions et les appels respectent les autorisations."), 12));
        panel.Children.Add(Label(T("Pour les API compatibles OpenAI/DeepSeek. OpenCode utilise sa propre configuration MCP."), 12));
        panel.Children.Add(add); panel.Children.Add(chooser); panel.Children.Add(fields); panel.Children.Add(Row(test, remove)); panel.Children.Add(info);
        Refresh(drafts.FirstOrDefault());
        return new(panel, Validate, Save);
    }
}

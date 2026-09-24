using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    GitHubUpdate? guiUpdate;
    readonly CancellationTokenSource updateLifetime = new();
    bool updatingApplication;

    void StartUpdateCheck()
    {
        Closed += (_, _) => updateLifetime.Cancel();
        if (!FeatureSettings.Read(state.FeaturesJson).GuiCheckUpdates || Environment.GetEnvironmentVariable("OHMYHARNESS_UI_SMOKE") != null) return;
        _ = CheckGuiUpdateAsync(true);
    }
    async Task CheckGuiUpdateAsync(bool quiet)
    {
        try
        {
            using var client = new HttpClient();
            guiUpdate = await new GitHubUpdates(client).CheckAsync(UpdateChannel.Gui, updateLifetime.Token);
            if (guiUpdate != null) ShowStatus(WorkflowText("Mise à jour GUI disponible : ", "GUI update available: ") + guiUpdate.Version + WorkflowText(" · Réglages > Général", " · Settings > General"));
        }
        catch (Exception ex)
        {
            if (!quiet) throw;
            AppLog.Write(AppLogLevel.Warning, "gui.update_check_failed", ex);
        }
    }
    (StackPanel Panel, Action<FeatureSettings> Save) BuildUpdateSettings()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label(WorkflowText("Mises à jour GitHub", "GitHub updates") + " · " + GitHubUpdates.CurrentVersion, 18));
        var auto = new CheckBox { Content = WorkflowText("Vérifier automatiquement au démarrage", "Check automatically at startup"), IsChecked = FeatureSettings.Read(state.FeaturesJson).GuiCheckUpdates };
        panel.Children.Add(auto);
        var info = Label(guiUpdate == null ? WorkflowText("Rechercher une version GUI plus récente.", "Check for a newer GUI release.") : "GUI " + guiUpdate.Version, 13);
        panel.Children.Add(info);
        var check = new Button { Content = WorkflowText("Rechercher", "Check") };
        var install = new Button { Content = WorkflowText("Télécharger et redémarrer", "Download and restart"), IsEnabled = guiUpdate != null && GitHubUpdates.CanInstall && GitHubUpdates.IsStandalone(typeof(App).Assembly) };
        panel.Children.Add(Row(check, install));
        panel.Children.Add(Label(WorkflowText("Les données sont conservées. Enregistrez vos réglages avant l’installation ; les changements non enregistrés seront annulés. Les outils seront fermés. L’ancien EXE est conservé dans .updates.", "Data is preserved. Save settings before installing; unsaved changes will be discarded. Tools will close. The previous EXE is kept in .updates."), 12));
        check.Click += async (_, _) => await Guard(async () =>
        {
            check.IsEnabled = false; install.IsEnabled = false; info.Text = WorkflowText("Recherche sur GitHub…", "Checking GitHub…");
            try
            {
                await CheckGuiUpdateAsync(false);
                info.Text = guiUpdate == null ? WorkflowText("Aucune release GUI compatible plus récente avec empreinte SHA-256.", "No newer compatible GUI release with a SHA-256 digest.") : "GUI " + guiUpdate.Version + " · " + guiUpdate.Page;
                install.IsEnabled = guiUpdate != null && GitHubUpdates.CanInstall && GitHubUpdates.IsStandalone(typeof(App).Assembly);
            }
            catch { info.Text = WorkflowText("Recherche impossible. Réessayez plus tard.", "Unable to check. Try again later."); throw; }
            finally { check.IsEnabled = true; }
        });
        install.Click += async (_, _) => await Guard(async () =>
        {
            if (guiUpdate == null || updatingApplication) return;
            bool Busy() => conversationRuns.Count > 0 || !string.IsNullOrWhiteSpace(composer.Text) || pendingImages.Count > 0
                || conversationDrafts.Any(d => d.Key != chat?.Id && (!string.IsNullOrWhiteSpace(d.Value.Text) || d.Value.Images.Count > 0));
            if (Busy()) throw new InvalidOperationException(WorkflowText("Terminez les agents et envoyez ou effacez les brouillons avant l’installation.", "Finish agents and send or clear drafts before installing."));
            updatingApplication = true; check.IsEnabled = install.IsEnabled = false;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(11) };
                var updater = new GitHubUpdates(client);
                var progress = new Progress<int>(value => info.Text = WorkflowText("Téléchargement : ", "Downloading: ") + value + "%");
                var staged = await updater.DownloadAsync(guiUpdate, UpdateChannel.Gui, Environment.ProcessPath!, progress, updateLifetime.Token);
                if (settingsWindow == null) return;
                if (Busy()) throw new InvalidOperationException(WorkflowText("Un agent ou brouillon est actif ; relancez l’installation après sa fin.", "An agent or draft is active; retry installation when finished."));
                scheduleTimer.Stop();
                try { GitHubUpdates.InstallAfterExit(staged, Environment.ProcessPath!, [], GitHubUpdates.IsStandalone(typeof(App).Assembly)); }
                catch { scheduleTimer.Start(); throw; }
                settingsWindow?.Close(); Close(); Application.Current.Exit();
            }
            finally { updatingApplication = false; check.IsEnabled = install.IsEnabled = true; }
        });
        return (panel, config => config.GuiCheckUpdates = auto.IsChecked == true);
    }
}

using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public sealed partial class TerminalUi
{
    GitHubUpdate? availableUpdate;
    bool updateBusy;
    async Task CheckStartupUpdate()
    {
        try
        {
            using var http = new HttpClient();
            var update = await new GitHubUpdates(http).CheckAsync(UpdateChannel.Cli, lifetime.Token);
            Post(() => { availableUpdate = update; if (update != null) notice = L("Mise à jour CLI ", "CLI update ") + update.Version + " · /update"; });
        }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "cli.update_check_failed", ex); }
    }
    async Task UpdateSettings()
    {
        if (updateBusy) return;
        updateBusy = true;
        try
        {
            var config = FeatureSettings.Read(workspace!.State.FeaturesJson);
            var action = await Prompt(L("Mises à jour CLI", "CLI updates"), "GitHub · " + GitHubUpdates.CurrentVersion,
                [new("check", L("Rechercher une mise à jour", "Check for updates")), new("auto", (config.CliCheckUpdates ? "[x] " : "[ ] ") + L("Vérification automatique au démarrage", "Automatic startup check"))]);
            if (action == "auto") { await SaveFeatures(c => c.CliCheckUpdates = !c.CliCheckUpdates); await Refresh(); return; }
            if (action != "check") return;
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(11) };
            var updater = new GitHubUpdates(http);
            var update = await updater.CheckAsync(UpdateChannel.Cli, lifetime.Token);
            Post(() => availableUpdate = update);
            if (update == null) { await Show("GitHub", L("Aucune release CLI compatible plus récente avec empreinte SHA-256.", "No newer compatible CLI release with a SHA-256 digest.")); return; }
            if (!GitHubUpdates.CanInstall || !GitHubUpdates.IsStandalone(typeof(TerminalUi).Assembly))
            { await Show("GitHub", update.Page + "\n" + L("Installation automatique disponible dans l’EXE Windows publié.", "Automatic installation is available in the published Windows EXE.")); return; }
            if (await Prompt(L("Installer la mise à jour ?", "Install update?"), $"{update.Version} · {update.Size / 1024 / 1024} MiB\n{update.Page}\n" + L("Télécharge et vérifie l’EXE, puis ferme les outils et redémarre. Données conservées.", "Downloads and verifies the EXE, then closes tools and restarts. Data is preserved."),
                [new("later", L("Plus tard", "Later")), new("install", L("Télécharger et redémarrer", "Download and restart"))]) != "install") return;
            if (runs.Values.Any(t => !t.IsCompleted) || drafts.Values.Any(t => !string.IsNullOrWhiteSpace(t)) || !string.IsNullOrWhiteSpace(editor.Text))
                throw new InvalidOperationException(L("Terminez les agents et envoyez ou effacez vos brouillons avant la mise à jour.", "Finish agents and send or clear drafts before updating."));
            var path = Environment.ProcessPath!;
            var progress = new Progress<int>(percent => Post(() => connectionProgress = L("Mise à jour : ", "Update: ") + percent + "%"));
            var staged = await updater.DownloadAsync(update, UpdateChannel.Cli, path, progress, lifetime.Token);
            if (runs.Values.Any(t => !t.IsCompleted) || drafts.Values.Any(t => !string.IsNullOrWhiteSpace(t)) || !string.IsNullOrWhiteSpace(editor.Text))
                throw new InvalidOperationException(L("Un agent ou brouillon est actif. Relancez /update une fois terminé.", "An agent or draft is active. Run /update again when finished."));
            var restart = new List<string> { "--database", client.Database, "--chat", chatId.ToString() };
            if (options.Theme != null) restart.AddRange(["--theme", options.Theme]);
            GitHubUpdates.InstallAfterExit(staged, path, restart, true);
            Post(() => quit = true);
        }
        finally { Post(() => { updateBusy = false; connectionProgress = ""; }); }
    }
}

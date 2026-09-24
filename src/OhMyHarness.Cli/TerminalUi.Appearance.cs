using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public sealed partial class TerminalUi
{
    async Task ChooseCliTheme(WorkspaceSnapshot snapshot, string requested = "")
    {
        var settings = FeatureSettings.Read(snapshot.State.FeaturesJson);
        var choices = new List<Choice> { new("shared", L("Suivre le thème de l’application", "Follow desktop theme")) };
        choices.AddRange(CliThemes.All.Select(t => new Choice(t.Id, t.Name, "CRT")));
        choices.AddRange(AppearanceThemes.All.Select(t => new Choice(t.Id, L(t.French, t.English))));
        string? selected = requested.Length > 0 ? requested : await Prompt(L("Thème du CLI", "CLI theme"),
            L("↑/↓ : aperçu immédiat. Entrée : enregistrer. Échap : revenir au thème précédent. /font pour la police et l’effet cathodique Windows Terminal.",
              "↑/↓: live preview. Enter: save. Esc: restore previous theme. /font for Windows Terminal fonts and CRT effects."), choices,
            previewTheme: true, selectedValue: options.Theme ?? settings.CliTheme);
        if (selected == null) return;
        if (!CliThemes.IsValid(selected)) throw new ArgumentException("Unknown CLI theme: " + selected);
        await client.State(s => { var config = FeatureSettings.Read(s.FeaturesJson); config.CliTheme = selected; s.FeaturesJson = config.Json(); });
        var fresh = await client.Snapshot(lifetime.Token);
        Post(() => { options.Theme = null; workspace = fresh; notice = L("Thème appliqué · /font pour la police et le CRT", "Theme applied · /font for fonts and CRT"); });
    }

    async Task ConfigureTerminalFont(WorkspaceSnapshot snapshot)
    {
        var config = FeatureSettings.Read(snapshot.State.FeaturesJson);
        var fontChoices = CliFonts.All.Select(f => new Choice(f.Face, f.Face, f.Description + (f.File != null ? " · embarquée / bundled" : " · système / system"))).ToList();
        fontChoices.Insert(0, new("default", L("Police par défaut du thème", "Theme default font")));
        fontChoices.Add(new("custom", L("Autre police installée…", "Other installed font…")));
        var face = await Prompt(L("Police CLI indépendante du thème", "CLI font independent of theme"),
            L("La police s’applique via un profil dans un nouvel onglet du terminal. Choisir une police ne change pas le thème.", "Fonts apply through a profile in a new terminal tab. Choosing a font does not change the theme."), fontChoices,
            selectedValue: config.CliFont.Length == 0 ? "default" : config.CliFont);
        if (face == null) return;
        if (face == "custom") face = await Prompt(L("Nom exact de la police installée", "Exact installed font name"), initial: config.CliFont);
        if (face == null) return;
        string? size = await Prompt(L("Taille de police (8–36)", "Font size (8–36)"), initial: config.CliFontSize.ToString());
        if (size == null) return;
        if (!int.TryParse(size, out int points) || points is < 8 or > 36) throw new ArgumentException("Taille de police invalide / Invalid font size.");
        config.CliFont = face == "default" ? "" : face.Trim(); config.CliFontSize = points; _ = config.Json();
        var theme = CliFonts.Apply(CliThemes.Resolve(options.Theme ?? config.CliTheme, config.Theme), config);
        string details = L($"Police : {theme.Font}, {theme.FontSize} pt. CRT : {(theme.Crt ? "oui" : "non")}.\nLa police est contrôlée par le terminal hôte. Le profil dédié applique police, curseur, couleurs et effet CRT dans un nouvel onglet Windows Terminal.\nLes terminaux macOS et autres conservent leur police : choisissez-la dans leurs préférences. Aucun téléchargement de police.",
            $"Font: {theme.Font}, {theme.FontSize} pt. CRT: {theme.Crt}.\nThe host terminal controls fonts. The dedicated profile applies font, cursor, colors and CRT in a new Windows Terminal tab.\nmacOS and other terminals retain their font: choose it in their preferences. No font downloads.");
        var choices = new List<Choice> { new("export", L("Enregistrer et exporter le profil + police", "Save and export profile + font"), "terminal-profiles/*.json · fonts/"), new("save", L("Enregistrer le choix", "Save selection")) };
        if (OperatingSystem.IsWindows()) choices.Insert(0, new("install", L("Enregistrer et installer police + profil", "Save and install font + profile"), L("Pour cet utilisateur, puis ouvrir un nouvel onglet", "For this user; then open a new tab")));
        choices.Add(new("close", L("Fermer", "Close")));
        var action = await Prompt(L("Police et effet cathodique", "Font and CRT effect"), details, choices);
        if (action is not ("install" or "export" or "save")) return;
        await SaveFeatures(c => { c.CliFont = config.CliFont; c.CliFontSize = config.CliFontSize; }); await Refresh();
        if (action == "save") return;
        await CliFonts.ExportAsync(theme.Font, PortableStorage.Root, lifetime.Token);
        if (action == "install") await CliFonts.InstallForUserAsync(theme.Font, PortableStorage.Root, lifetime.Token);
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        var assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? typeof(TerminalUi).Assembly.Location : null;
        var directory = CurrentProject?.GetSourceFolders().FirstOrDefault(Directory.Exists) ?? Environment.CurrentDirectory;
        var fragment = TerminalProfiles.Build(theme, executable, client.Database, directory, assembly);
        var portable = await TerminalProfiles.SaveAsync(fragment, Path.Combine(PortableStorage.Root, "terminal-profiles"), lifetime.Token);
        string destination = portable;
        if (action == "install")
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows Terminal profiles require Windows.");
            destination = await TerminalProfiles.SaveAsync(fragment, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows Terminal", "Fragments", "OhMyHarness"), lifetime.Token);
        }
        await Show(L("Profil prêt", "Profile ready"), destination + "\n\n" + L(
            "Profil : OhMyHarness · " + theme.Name + "\nOuvrez ce profil dans un nouvel onglet Windows Terminal (relancez le terminal si nécessaire). La session actuelle continue avec sa police actuelle.\nCopie portable : " + portable + "\nAprès déplacement du dossier portable, régénérez ce profil. Si la police n’est pas installée, le terminal utilise sa police de secours.",
            "Profile: OhMyHarness · " + theme.Name + "\nOpen this profile in a new Windows Terminal tab (restart the terminal if needed). This session keeps its current font.\nPortable copy: " + portable + "\nRegenerate after moving the portable folder. Missing fonts use the terminal fallback."));
    }
}

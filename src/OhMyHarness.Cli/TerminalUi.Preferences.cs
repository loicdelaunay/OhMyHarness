using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public sealed partial class TerminalUi
{
    Task SaveFeatures(Action<FeatureSettings> update) => client.State(state => { var settings = FeatureSettings.Read(state.FeaturesJson); update(settings); state.FeaturesJson = settings.Json(); });
    async Task<(int Provider, string Model)?> PickAuxiliaryModel(WorkspaceSnapshot snapshot, bool vision)
    {
        var providers = snapshot.Providers.Where(p => !p.IsComposite && (!vision || !p.IsOpenCode)).ToList();
        var id = await Prompt("Fournisseur / Provider", choices: providers.Select(p => new Choice(p.Id.ToString(), p.Name)).ToList());
        if (id == null) return null;
        var provider = providers.Single(p => p.Id == int.Parse(id));
        var models = ModelCatalog.GetModelsForProvider(provider);
        var model = await Prompt("Modèle / Model", choices: models.Select(m => new Choice(m, m)).Prepend(new("custom", "Saisir un modèle / Custom model")).ToList());
        if (model == "custom") model = await Prompt("Modèle / Model", initial: provider.Model);
        return string.IsNullOrWhiteSpace(model) ? null : (provider.Id, model.Trim());
    }
    async Task NamingSettings(WorkspaceSnapshot snapshot)
    {
        var config = FeatureSettings.Read(snapshot.State.FeaturesJson);
        var action = await Prompt("Nommage / Naming", "Le modèle reçoit un extrait textuel après la première réponse. / The model receives a text excerpt after the first response.", [new("toggle", (config.AutoNameConversations ? "[x] " : "[ ] ") + "Automatique / Automatic"), new("model", "Modèle / Model · " + config.NamingModel)]);
        if (action == "model" || action == "toggle" && !config.AutoNameConversations && (config.NamingProviderId == 0 || config.NamingModel.Length == 0))
        {
            if (await PickAuxiliaryModel(snapshot, false) is not { } selected) return;
            await SaveFeatures(c => { c.NamingProviderId = selected.Provider; c.NamingModel = selected.Model; if (action == "toggle") c.AutoNameConversations = true; });
        }
        else if (action == "toggle") await SaveFeatures(c => c.AutoNameConversations = !config.AutoNameConversations);
    }
    async Task VisionSettings(WorkspaceSnapshot snapshot)
    {
        var config = FeatureSettings.Read(snapshot.State.FeaturesJson);
        var action = await Prompt("Bypass image AI", "Coordonnées relatives à l’image ; formes décrites, pas de fichiers découpés. / Image-relative coordinates, described shapes rather than cropped files.", [new("model", "Modèle / Model · " + config.VisionModel), new("instruction", "Instruction personnalisée / Custom instruction"), new("components", (config.VisionComponents ? "[x] " : "[ ] ") + "Composants et coordonnées / Components and bounds")]);
        if (action == "model" && await PickAuxiliaryModel(snapshot, true) is { } selected) await SaveFeatures(c => { c.VisionProviderId = selected.Provider; c.VisionModel = selected.Model; });
        if (action == "instruction") { var text = await Prompt("Instruction", initial: config.VisionInstruction); if (text != null) await SaveFeatures(c => c.VisionInstruction = text); }
        if (action == "components") await SaveFeatures(c => c.VisionComponents = !config.VisionComponents);
    }
    async Task LogSettings(WorkspaceSnapshot snapshot)
    {
        var config = FeatureSettings.Read(snapshot.State.FeaturesJson);
        var action = await Prompt("Logs", AppLog.DirectoryPath, [new("toggle", (config.LogsEnabled ? "[x] " : "[ ] ") + "Activés / Enabled"), new("level", "Sévérité / Severity · " + config.LogLevel), new("days", "Conservation / Retention · " + config.LogRetentionDays + " jours / days")]);
        if (action == "toggle") await SaveFeatures(c => c.LogsEnabled = !config.LogsEnabled);
        if (action == "level") { var level = await Prompt("Sévérité / Severity", choices: Enum.GetNames<AppLogLevel>().Select(l => new Choice(l, l)).ToList()); if (level != null) await SaveFeatures(c => c.LogLevel = level); }
        if (action == "days") { var days = await Prompt("Jours / Days · 1–365", initial: config.LogRetentionDays.ToString()); if (days != null && int.TryParse(days, out var value) && value is >= 1 and <= 365) await SaveFeatures(c => c.LogRetentionDays = value); }
    }
}

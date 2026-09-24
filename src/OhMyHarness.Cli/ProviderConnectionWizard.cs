using OhMyHarness.Core;

namespace OhMyHarness.Cli;

public sealed record ConnectionStep(string Title, string Body = "", List<Choice>? Choices = null, bool Secret = false, string Initial = "");
public sealed record ProviderPreset(string Id, string Name, string Url, string Kind, bool OptionalKey);

public sealed class ProviderConnectionWizard(HttpClient http,
    Func<ConnectionStep, Task<string?>> prompt, Func<Provider, string, CancellationToken, Task<int>> save,
    Action<string>? progress = null)
{
    public static IReadOnlyList<ProviderPreset> Presets { get; } = [
        new("openai", "OpenAI", "https://api.openai.com/v1", "openai", false),
        new("deepseek", "DeepSeek", "https://api.deepseek.com", "openai", false),
        new("compatible", "Autre API compatible OpenAI / Other compatible API", "", "openai", true),
        new("local", "API locale compatible OpenAI / Local compatible API", "", "openai", true),
        new("opencode", "OpenCode", "http://127.0.0.1:4096", "opencode", true)
    ];
    public async Task<int?> RunAsync(CancellationToken ct)
    {
        var type = await prompt(new("1/4 · Type de fournisseur / Provider type", Choices: Presets.Select(p => new Choice(p.Id, p.Name, p.Url)).ToList()));
        if (type == null) return null;
        var preset = Presets.Single(p => p.Id == type);
        var draft = new Provider { Name = preset.Name.Split(" / ")[0], Kind = preset.Kind, BaseUrl = preset.Url, Model = "", Username = preset.Kind == "opencode" ? "opencode" : "" };
        // Credentials stay in memory until the final confirmation, including during discovery.
        var key = await prompt(new("2/4 · " + (draft.IsOpenCode ? "Mot de passe serveur / Server password" : "Clé API / API key"),
            preset.OptionalKey ? "Saisie masquée. Vide si aucune authentification. / Masked. Empty if no authentication."
                : "Saisie masquée ; clé chiffrée après validation. / Masked; encrypted after confirmation.", Secret: true));
        if (key == null) return null;
        while (!preset.OptionalKey && string.IsNullOrWhiteSpace(key))
        {
            key = await prompt(new("2/4 · Clé API requise / API key required", Secret: true));
            if (key == null) return null;
        }
        while (true)
        {
            var url = await prompt(new("2/4 · URL de l’API / API URL", "HTTP(S), avec /v1 si nécessaire. / Include /v1 when required.", Initial: draft.BaseUrl));
            if (url == null) return null;
            try { _ = ChatEngine.Endpoint(url.Trim(), "models"); draft.BaseUrl = url.Trim().TrimEnd('/'); break; }
            catch (ArgumentException) { progress?.Invoke("URL HTTP(S) invalide / Invalid HTTP(S) URL"); }
        }
        if (draft.IsOpenCode)
        {
            var username = await prompt(new("2/4 · Utilisateur OpenCode / OpenCode username", Initial: "opencode"));
            if (username == null) return null;
            draft.Username = string.IsNullOrWhiteSpace(username) ? "opencode" : username.Trim();
        }
        List<string> models = [];
        while (models.Count == 0)
        {
            var mode = await prompt(new("3/4 · Modèles / Models", "Détecter via l’API ou saisir les identifiants exacts. / Discover via API or enter exact IDs.",
                [new("auto", "Détection automatique / Auto-detect"), new("manual", "Saisie manuelle / Enter manually")]));
            if (mode == null) return null;
            if (mode == "auto")
            {
                progress?.Invoke("Détection des modèles… / Detecting models…");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    models = ProviderModels.Normalize(draft.IsOpenCode
                        ? (await new OpenCodeEngine(http).ModelsAsync(draft, key, null, timeout.Token)).Select(m => m.Reference)
                        : await new ChatEngine(http).ModelsAsync(draft, key, timeout.Token));
                    if (models.Count == 0) throw new InvalidOperationException("Aucun modèle retourné / No models returned");
                }
                catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidOperationException || error is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    // Do not echo server bodies, which can contain credentials or sensitive context.
                    var reason = error is OperationCanceledException ? "Délai dépassé / Timed out" : error is HttpRequestException ? "Erreur HTTP / HTTP error" : "Liste indisponible / Models unavailable";
                    var retry = await prompt(new("Détection impossible / Discovery failed", reason + ". Vérifiez URL et clé ; vous pouvez saisir les modèles. / Check URL and key or enter models manually.",
                        [new("manual", "Saisie manuelle / Enter manually"), new("retry", "Réessayer / Retry"), new("cancel", "Annuler / Cancel")]));
                    if (retry is null or "cancel") return null;
                    if (retry == "retry") continue;
                    mode = "manual";
                }
            }
            if (mode == "manual")
            {
                var value = await prompt(new("3/4 · Identifiants des modèles / Model IDs", "Un ou plusieurs modèles séparés par des virgules. OpenCode : fournisseur/modèle. / Comma-separated IDs. OpenCode: provider/model."));
                if (value == null) return null;
                models = ProviderModels.Normalize(value.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
            }
        }
        var active = models.Count == 1 ? models[0] : await prompt(new("3/4 · Modèle par défaut / Default model", "Tous les modèles seront disponibles via /models. / All models will be available via /models.", models.Select(m => new Choice(m, m)).ToList()));
        if (active == null) return null;
        draft.Model = active;
        ProviderModels.Refresh(draft, models); ProviderModels.Select(draft, models);
        var name = await prompt(new("4/4 · Nom de cette connexion / Connection name", "Plusieurs connexions du même type sont possibles. / Multiple connections of the same type are supported.", Initial: draft.Name));
        if (name == null) return null;
        if (!string.IsNullOrWhiteSpace(name)) draft.Name = name.Trim();
        var decision = await prompt(new("4/4 · Valider la connexion / Confirm connection", $"{draft.Name}\n{draft.BaseUrl}\n{models.Count} modèle(s) · {draft.Model}\n" + (key.Length == 0 ? "Sans clé / No key" : "Clé fournie (masquée) / Key provided (hidden)"),
            [new("save", "Valider et utiliser / Save and use"), new("cancel", "Annuler / Cancel")]));
        if (decision != "save") return null;
        ct.ThrowIfCancellationRequested();
        return await save(draft, key, ct);
    }
}

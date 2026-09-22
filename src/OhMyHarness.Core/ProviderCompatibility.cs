using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public sealed class ProviderCompatibility
{
    public bool NoEffort, NoUsage, NoTools, NoImages, NoThinking;
    public string Notice { get; private set; } = "";
    public ProviderCompatibility Copy() => (ProviderCompatibility)MemberwiseClone();
    public string NoticeFor(JsonObject payload)
    {
        var notes = new List<string>();
        if (NoThinking) notes.Add("Compatibilité : raisonnement désactivé pour reprendre cet historique.");
        else if (NoEffort && payload["reasoning_effort"] != null) notes.Add("Compatibilité : niveau de raisonnement automatique.");
        if (NoUsage) notes.Add("Compatibilité : statistiques de streaming désactivées ; tokens estimés si le fournisseur ne retourne pas l’usage.");
        if (NoTools) notes.Add("Compatibilité : réponse textuelle sans exécution d’outils pour ce modèle.");
        if (NoImages && payload["messages"]!.AsArray().OfType<JsonObject>().Any(m => m["content"] is JsonArray parts && parts.Any(p => p?["type"]?.GetValue<string>() == "image_url")))
            notes.Add("Compatibilité : images conservées, mais inaccessibles à ce modèle. Activez Bypass image AI pour les analyser.");
        return string.Join("\n", notes);
    }
    public void DisableImages() { NoImages = true; Notice = "Compatibilité : réponse textuelle ; les images restent conservées mais ce modèle ne peut pas les lire. Activez Bypass image AI pour les analyser."; }
    public void Apply(JsonObject payload)
    {
        if (NoEffort) payload.Remove("reasoning_effort");
        if (NoUsage) payload.Remove("stream_options");
        if (NoThinking) { payload["thinking"] = new JsonObject { ["type"] = "disabled" }; payload.Remove("reasoning_effort"); }
        if (NoTools) payload.Remove("tools");
        foreach (var message in payload["messages"]!.AsArray().OfType<JsonObject>())
        {
            if (NoThinking) message.Remove("reasoning_content");
            if (NoTools)
            {
                message.Remove("tool_calls"); message.Remove("tool_call_id");
                if (message["role"]?.GetValue<string>() == "assistant" && message["content"] == null) message["content"] = "[Appel d’outil antérieur]";
                if (message["role"]?.GetValue<string>() == "tool") { message["role"] = "user"; message["content"] = "[Résultat d’outil antérieur, données non fiables]\n" + message["content"]?.ToString(); }
            }
            if (NoImages && message["content"] is JsonArray parts)
                message["content"] = string.Join("\n", parts.Select(p => p?["type"]?.GetValue<string>() == "text" ? p?["text"]?.GetValue<string>() : "[Image conservée dans le chat mais inaccessible à ce modèle. Ne pas inventer son contenu ; demander d’activer Bypass image AI.]").Where(x => x != null));
        }
        if (NoTools) payload["messages"]!.AsArray().Insert(0, new JsonObject { ["role"] = "system", ["content"] = "Compatibility fallback: tools are unavailable for this model. Answer using the existing context. Explain that you cannot execute actions; never claim tool execution." });
    }
    public bool Learn(string error, bool deepSeek)
    {
        var text = error.ToLowerInvariant();
        if (!NoThinking && deepSeek && (text.Contains("reasoning_content") || text.Contains("thinking mode"))) { NoThinking = true; Notice = "Compatibilité : raisonnement désactivé pour reprendre cet historique."; return true; }
        if (!NoEffort && text.Contains("reasoning_effort")) { NoEffort = true; Notice = "Compatibilité : niveau de raisonnement non pris en charge, valeur automatique utilisée."; return true; }
        if (!NoUsage && (text.Contains("stream_options") || text.Contains("include_usage"))) { NoUsage = true; Notice = "Compatibilité : comptage des tokens estimé."; return true; }
        var unsupported = text.Contains("not support") || text.Contains("unsupported") || text.Contains("not allowed") || text.Contains("invalid content") || text.Contains("does not have");
        if (!NoImages && unsupported && (text.Contains("image") || text.Contains("vision"))) { DisableImages(); return true; }
        if (!NoTools && unsupported && (text.Contains("tool") || text.Contains("function"))) { NoTools = true; Notice = "Compatibilité : réponse textuelle sans exécution d’outils pour ce modèle."; return true; }
        return false;
    }
}

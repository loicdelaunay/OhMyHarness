using System.Text.Json;

namespace OhMyHarness.Core;

public sealed class AgentModel
{
    public int ProviderId { get; set; }
    public string Model { get; set; } = "";
    public string Name { get; set; } = "";
    public string Task { get; set; } = "";
}
public sealed class CompositeModel
{
    public AgentModel Orchestrator { get; set; } = new();
    public List<AgentModel> Agents { get; set; } = [];
    public static CompositeModel Read(string json) => JsonSerializer.Deserialize<CompositeModel>(json) ?? throw new ArgumentException("Modèle composé invalide.");
    public string Json() => JsonSerializer.Serialize(this);
    public void Validate(IEnumerable<Provider> providers)
    {
        var available = providers.ToDictionary(x=>x.Id);
        if (Agents.Count is < 1 or > 6) throw new ArgumentException("Un modèle composé requiert 1 à 6 sous-agents.");
        foreach(var assignment in Agents.Prepend(Orchestrator))
            if (!available.TryGetValue(assignment.ProviderId,out var provider) || provider.IsComposite || string.IsNullOrWhiteSpace(assignment.Model))
                throw new ArgumentException("Choisissez un fournisseur enregistré et un modèle pour chaque agent (sans composition imbriquée).");
        if(Agents.Any(x=>string.IsNullOrWhiteSpace(x.Name)||x.Name.Length>80||string.IsNullOrWhiteSpace(x.Task)||x.Task.Length>8000) || Agents.Select(x=>x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Agents.Count)
            throw new ArgumentException("Sous-agents : noms uniques (80 caractères) et tâches requises (8000 caractères).");
    }
    public static Provider Resolve(AgentModel assignment, IEnumerable<Provider> providers)
    {
        var source=providers.SingleOrDefault(x=>x.Id==assignment.ProviderId && !x.IsComposite) ?? throw new InvalidOperationException("Fournisseur du modèle composé introuvable.");
        var copy=JsonSerializer.Deserialize<Provider>(JsonSerializer.Serialize(source))!;
        copy.Model=assignment.Model;
        if(ModelCatalog.GetDefaultContextLimit(copy.Model) is int limit) copy.ContextLimit=limit;
        return copy;
    }
}
